using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Executions;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Tests.TestSupport.Fakes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.Executor;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data.Common;

namespace FlowEngine.Application.Tests.Executions;

/// <summary>
/// 幂等并发回归测试（Phase 3 #10 修复）：同幂等键的并发请求不得重复触发真实执行（至多一次），
/// 落败者须复用胜者的真实执行结果，绝不返回合成成功。
/// 并发竞态依赖真实数据库的唯一约束才能可靠复现：核心用例使用文件型 SQLite（多连接可见性正确、
/// 唯一约束可靠），InMemory 提供程序不强制唯一约束且并发非线程安全，无法可靠复现。
/// 为去除 OS 调度带来的偶发抖动，并发用例用引擎侧信号将「抢占」与「启动」两阶段解耦，
/// 使落败者稳定进入等待复用路径，但仍经由真实的 TryGetOrRegisterAsync 唯一约束竞态。
/// </summary>
public sealed class ExecutionServiceConcurrencyIdempotencyTests : IDisposable
{
    // 文件型 SQLite：多连接共享同一库，唯一约束与已提交数据的跨连接可见性均可靠。
    private readonly string _dbFile = Path.Combine(Path.GetTempPath(), $"fe_idem_{Guid.NewGuid():N}.db");
    private readonly List<FlowEngineDbContext> _contexts = new();
    private readonly SharedStartCounter _startCounter = new();

    // 引擎侧信号：放行胜者的真实启动（写记录 + 更新幂等键）。落败者不会调用 StartAsync，故不会阻塞。
    private readonly TaskCompletionSource<object?> _startSignal =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dispose()
    {
        foreach (var context in _contexts)
        {
            context.Dispose();
        }

        // 连接释放后删除临时库（尽力而为，避免测试环境污染）。
        try
        {
            if (File.Exists(_dbFile))
            {
                File.Delete(_dbFile);
            }
        }
        catch (IOException)
        {
            // 忽略删除失败（如仍被防病毒扫描占用），不影响测试结论。
        }
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentSameIdempotencyKey_StartsExecutionExactlyOnce_AndBothReturnSameRealResult()
    {
        var ct = TestContext.Current.CancellationToken;

        // 预置工作流（落库于共享文件库，供两个请求上下文读取）。
        var workflow = CreateTestWorkflow();
        using (var seedContext = CreateContext())
        {
            seedContext.Database.EnsureCreated();
            seedContext.Workflows.Add(workflow);
            await seedContext.SaveChangesAsync(ct);
        }

        // 两个并发请求各自拥有独立 DbContext + 真实幂等服务（共享同一文件库的唯一约束）。
        var (serviceA, contextA, engineA) = CreateParticipant();
        var (serviceB, contextB, engineB) = CreateParticipant();

        var key = "concurrent-key";
        var taskA = serviceA.ExecuteAsync(workflow.Id, idempotencyKey: key, cancellationToken: ct);
        var taskB = serviceB.ExecuteAsync(workflow.Id, idempotencyKey: key, cancellationToken: ct);

        // 让两个请求都完成「抢占」阶段：胜者阻塞在 StartAsync，落败者进入等待轮询。
        await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        // 放行胜者：写入真实记录并把幂等键从 claimId 更新为真实执行 id。
        _startSignal.TrySetResult(null);

        var results = await Task.WhenAll(taskA, taskB).WaitAsync(TimeSpan.FromSeconds(30), ct);

        // 核心断言：并发同键只触发一次真实执行（至多一次），不重复发信/写外部系统。
        Assert.Equal(1, _startCounter.Count);

        var resultA = results[0];
        var resultB = results[1];
        Assert.NotNull(resultA);
        Assert.NotNull(resultB);
        // 两请求最终都返回同一真实执行结果（胜者写入的真实记录），绝不返回合成对象。
        Assert.Equal(resultA!.Id, resultB!.Id);
        Assert.Equal(nameof(ExecutionStatus.Completed), resultA.Status);
    }

    /// <summary>
    /// 控制流锁定：幂等键已被他请求抢占（注册的 id ≠ 本请求 claimId）时，
    /// 本请求不应启动新执行，而应复用抢占者的真实执行记录。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_IdempotencyKeyClaimedByOther_ReusesRealRecord_WithoutStarting()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var dbContext = new FlowEngineDbContext(options);
        var userContext = new FakeUserContext();
        userContext.Roles = [RoleConstants.Admin];

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "Control Test Workflow",
            ProjectId = Guid.NewGuid(),
            Nodes = [],
            Connections = [],
            CreatedBy = "test",
            Version = 1,
            IsActive = true,
        };
        dbContext.Workflows.Add(workflow);

        var winnerId = Guid.NewGuid();
        dbContext.ExecutionRecords.Add(new ExecutionRecord
        {
            Id = winnerId,
            WorkflowDefinitionId = workflow.Id,
            Status = ExecutionStatus.Completed,
            StartedAt = DateTime.UtcNow.AddMinutes(-1),
            CompletedAt = DateTime.UtcNow,
            NodeRecords = [],
        });
        await dbContext.SaveChangesAsync(ct);

        // 假服务模拟「第二次注册返回他人 id」与「查询返回他人 id」。
        var idempotencyService = new ClaimedByOtherIdempotencyService(winnerId);
        var engine = new CountingEngine();
        var eventBus = new RecordingEventBus();
        var auditFactory = new AuditEventFactory(userContext);
        var resourceAuthorization = new StubResourceAuthorizationService();
        var service = new ExecutionService(
            engine,
            dbContext,
            idempotencyService,
            AuthorizationGuardFactory.Create(userContext, resourceAuthorization),
            eventBus,
            auditFactory,
            new ExecutionCancellationRegistry());

        var result = await service.ExecuteAsync(
            workflow.Id, idempotencyKey: "claimed-key", cancellationToken: ct);

        Assert.NotNull(result);
        Assert.Equal(winnerId, result!.Id);
        // 落败者不启动新执行，复用胜者真实记录。
        Assert.Equal(0, engine.StartCount);
    }

    private FlowEngineDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseSqlite($"Data Source={_dbFile}")
            .AddInterceptors(new BusyTimeoutInterceptor())
            .Options;
        return new FlowEngineDbContext(options);
    }

    private (ExecutionService service, FlowEngineDbContext dbContext, RecordingEngine engine) CreateParticipant()
    {
        var dbContext = CreateContext();
        _contexts.Add(dbContext);
        var userContext = new FakeUserContext();
        userContext.Roles = [RoleConstants.Admin];
        var engine = new RecordingEngine(dbContext, _startCounter, _startSignal);
        var eventBus = new RecordingEventBus();
        var auditFactory = new AuditEventFactory(userContext);
        var resourceAuthorization = new StubResourceAuthorizationService();
        var idempotencyService = new ExecutionIdempotencyService(dbContext, NullLogger<ExecutionIdempotencyService>.Instance);
        var service = new ExecutionService(
            engine,
            dbContext,
            idempotencyService,
            AuthorizationGuardFactory.Create(userContext, resourceAuthorization),
            eventBus,
            auditFactory,
            new ExecutionCancellationRegistry());
        return (service, dbContext, engine);
    }

    private static Workflow CreateTestWorkflow()
    {
        return new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "Concurrent Test Workflow",
            ProjectId = Guid.NewGuid(),
            Nodes = [],
            Connections = [],
            CreatedBy = "test",
            Version = 1,
            IsActive = true,
        };
    }

    /// <summary>
    /// 跨并发请求共享的启动计数器（Interlocked，确保恰好一次语义可被断言）。
    /// </summary>
    private sealed class SharedStartCounter
    {
        private int _count;

        public int Count => _count;

        public int Increment() => Interlocked.Increment(ref _count);
    }

    /// <summary>
    /// 真实引擎桩：收到放行信号后才写入一条真实 <see cref="ExecutionRecord"/>（用真实 id），
    /// 以便落败者的 <c>WaitForRealExecutionAsync</c> 能查到胜者的真实记录；并自增共享计数器。
    /// 在信号前阻塞，使并发用例可确定性地解耦「抢占」与「启动」两阶段。
    /// </summary>
    private sealed class RecordingEngine(
        FlowEngineDbContext dbContext, SharedStartCounter counter, TaskCompletionSource<object?> startSignal) : IEngine
    {
        public async Task<ExecutionId> StartAsync(
            Guid workflowDefinitionId, object? triggerPayload = null, CancellationToken cancellationToken = default)
        {
            // 等待信号：胜者在此阻塞，直到两个请求的抢占阶段均已完成。
            await startSignal.Task.ConfigureAwait(false);

            counter.Increment();
            var executionId = Guid.NewGuid();
            dbContext.ExecutionRecords.Add(new ExecutionRecord
            {
                Id = executionId,
                WorkflowDefinitionId = workflowDefinitionId,
                Status = ExecutionStatus.Completed,
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
                NodeRecords = [],
            });
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ExecutionId.From(executionId);
        }

        public Task<ExecutionId> StartAsync(Guid workflowDefinitionId, Workflow preloadedWorkflow, object? triggerPayload = null, CancellationToken cancellationToken = default)
            => StartAsync(workflowDefinitionId, triggerPayload, cancellationToken);
    }

    /// <summary>
    /// 计数引擎（不写记录），用于验证「不启动」路径。
    /// </summary>
    private sealed class CountingEngine : IEngine
    {
        public int StartCount { get; private set; }

        public Task<ExecutionId> StartAsync(
            Guid workflowDefinitionId, object? triggerPayload = null, CancellationToken cancellationToken = default)
        {
            StartCount++;
            return Task.FromResult(ExecutionId.From(Guid.NewGuid()));
        }

        public Task<ExecutionId> StartAsync(Guid workflowDefinitionId, Workflow preloadedWorkflow, object? triggerPayload = null, CancellationToken cancellationToken = default)
            => StartAsync(workflowDefinitionId, triggerPayload, cancellationToken);
    }

    /// <summary>
    /// 假幂等服务：模拟幂等键已被他请求抢占（注册与查询均返回他人 id）。
    /// </summary>
    private sealed class ClaimedByOtherIdempotencyService(Guid otherId) : IExecutionIdempotencyService
    {
        public Task<Guid?> TryGetOrRegisterAsync(
            string idempotencyKey, Guid executionId, TimeSpan? ttl = null, CancellationToken ct = default)
            => Task.FromResult<Guid?>(otherId);

        public Task<Guid?> TryGetExistingAsync(string idempotencyKey, CancellationToken ct = default)
            => Task.FromResult<Guid?>(otherId);

        public Task CleanupExpiredAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>
    /// 并发写场景下设置 SQLite 忙等待超时，避免「database is locked」误报（唯一约束仍可正确触发）。
    /// </summary>
    private sealed class BusyTimeoutInterceptor : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
            => Apply(connection);

        public override Task ConnectionOpenedAsync(
            DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken)
        {
            Apply(connection);
            return Task.CompletedTask;
        }

        private static void Apply(DbConnection connection)
        {
            if (connection is SqliteConnection sqlite)
            {
                using var command = sqlite.CreateCommand();
                command.CommandText = "PRAGMA busy_timeout = 10000;";
                command.ExecuteNonQuery();
            }
        }
    }

    private sealed class StubResourceAuthorizationService : IResourceAuthorizationService
    {
        public Task<bool> CanAccessWorkflowAsync(Guid userId, Guid workflowId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> CanAccessCredentialAsync(Guid userId, Guid credentialId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> CanAccessExecutionAsync(Guid userId, Guid executionId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> CanAccessTriggerAsync(Guid userId, Guid triggerId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public bool ShouldMaskCredentialValues(IReadOnlyList<string> roles) => false;
    }
}
