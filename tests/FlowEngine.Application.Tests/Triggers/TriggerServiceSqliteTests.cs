using FlowEngine.Application.Authorization;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Triggers;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Application.Tests.TestSupport.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Application.Tests.Triggers;

/// <summary>
/// 关系型（SQLite 内存库）集成测试，专门覆盖 TriggerService 的
/// <c>DeleteByWorkflowDefinitionIdAsync</c> 中 <c>ExecuteDeleteAsync</c> 批量删除路径
/// （<c>dbContext.Database.IsRelational() == true</c>）。
/// 既有测试全部使用 InMemory 提供程序，<c>IsRelational()</c> 为 false，永远走不到关系型分支；
/// 本测试通过内存 SQLite 将 <c>IsRelational()</c> 翻为 true，并用 EF Core 命令日志证明
/// 实际执行了单语句批量删除，而非 InMemory 的退化路径。
/// </summary>
public sealed class TriggerServiceSqliteTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly RecordingEventBus _eventBus;
    private readonly TriggerService _service;
    private readonly List<string> _log = [];

    public TriggerServiceSqliteTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseSqlite("DataSource=:memory:") // IsRelational() == true
            .LogTo(msg => _log.Add(msg), LogLevel.Information) // 捕获 EF Core 命令 SQL
            .Options;

        _dbContext = new FlowEngineDbContext(options);
        _dbContext.Database.OpenConnection(); // 必须保持一个连接打开，内存 SQLite 才存活
        _dbContext.Database.EnsureCreated();

        _eventBus = new RecordingEventBus();
        var userContext = new FakeUserContext { Roles = [RoleConstants.Admin] };
        var auditFactory = new AuditEventFactory(userContext);
        var scheduleManager = new FakeScheduleManager();
        var resourceAuthorization = new StubResourceAuthorizationService();
        _service = new TriggerService(
            _dbContext,
            _eventBus,
            auditFactory,
            scheduleManager,
            AuthorizationGuardFactory.Create(userContext, resourceAuthorization),
            new WebhookRouteService(_dbContext),
            NullLogger<TriggerService>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task DeleteByWorkflowDefinitionIdAsync_RelationalExecuteDelete_RemovesTriggers()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = Guid.NewGuid();
        var otherWorkflowId = Guid.NewGuid();

        var workflow = CreateTestWorkflow(workflowId);
        var otherWorkflow = CreateTestWorkflow(otherWorkflowId);
        _dbContext.Workflows.AddRange(workflow, otherWorkflow);

        var trigger1 = CreateTestTrigger(TriggerType.Schedule, workflowId);
        var trigger2 = CreateTestTrigger(TriggerType.Webhook, workflowId);
        var otherTrigger = CreateTestTrigger(TriggerType.Schedule, otherWorkflowId);
        _dbContext.Triggers.AddRange(trigger1, trigger2, otherTrigger);
        await _dbContext.SaveChangesAsync(ct);

        // 仅捕获 Delete 期间产生的命令（建表 / 插入已在之前发生）
        _log.Clear();
        await _service.DeleteByWorkflowDefinitionIdAsync(workflowId, ct);

        // ExecuteDeleteAsync 不会同步清理变更跟踪器中的实体，重新查询前先清空
        _dbContext.ChangeTracker.Clear();

        var remainingForWorkflow = await _dbContext.Triggers
            .Where(t => t.WorkflowDefinitionId == workflowId)
            .ToListAsync(ct);
        Assert.Empty(remainingForWorkflow);

        var remainingForOther = await _dbContext.Triggers
            .Where(t => t.WorkflowDefinitionId == otherWorkflowId)
            .ToListAsync(ct);
        Assert.Single(remainingForOther);

        // 关系型 ExecuteDeleteAsync 只生成「一条」DELETE 语句（针对该工作流，无论删多少行）。
        // 若是 InMemory 退化路径，根本不会产生任何 SQL DELETE（全程无 SQL）；
        // 即便退化路径跑在关系型提供程序上，RemoveRange 也会逐行生成 N 条 DELETE。
        var deleteCount = _log.Count(l => l.Contains("DELETE FROM") && l.Contains("triggers"));
        Assert.Equal(1, deleteCount);
    }

    private static Trigger CreateTestTrigger(TriggerType type, Guid? workflowId = null)
    {
        return new Trigger
        {
            WorkflowDefinitionId = workflowId ?? Guid.NewGuid(),
            WorkflowVersion = 1,
            Type = type,
            Name = $"Test {type}",
            IsActive = true,
            Settings = new TriggerSettings(),
        };
    }

    private static Workflow CreateTestWorkflow(Guid? id = null)
    {
        return new Workflow
        {
            Id = id ?? Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Name = "Test Workflow",
            CreatedBy = "test-user",
            IsActive = true,
            Nodes = [],
            Connections = [],
        };
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
