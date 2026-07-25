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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Application.Tests.Executions;

/// <summary>
/// D-11/D-12：执行列表查询投影（不物化 NodeRecords 大 JSON 列）+ keyset 分页
/// （WHERE StartedAt &lt; lastSeen ORDER BY StartedAt DESC）。
/// </summary>
public sealed class ExecutionServiceKeysetPagingTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly FakeUserContext _userContext;
    private readonly List<string> _sqlLog = [];
    private readonly ExecutionService _service;

    public ExecutionServiceKeysetPagingTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseSqlite("DataSource=:memory:")
            .LogTo(m => _sqlLog.Add(m), Microsoft.Extensions.Logging.LogLevel.Information)
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _userContext = new FakeUserContext();
        _userContext.Roles = [RoleConstants.Admin];
        var engine = new CapturingEngine();
        var eventBus = new RecordingEventBus();
        var auditFactory = new AuditEventFactory(_userContext);
        var resourceAuthorization = new StubResourceAuthorizationService(_userContext);
        var idempotencyService = new StubIdempotencyService();
        var cancellationRegistry = new ExecutionCancellationRegistry();
        _service = new ExecutionService(
            engine,
            _dbContext,
            idempotencyService,
            AuthorizationGuardFactory.Create(_userContext, resourceAuthorization),
            eventBus,
            auditFactory,
            cancellationRegistry);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetByWorkflowAsync_DoesNotSelectNodeRecordsColumn()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = SeedWorkflow(out var records);
        await _dbContext.SaveChangesAsync(ct);

        _sqlLog.Clear();
        var result = await _service.GetByWorkflowAsync(workflow.Id, cancellationToken: ct);

        Assert.NotEmpty(result.Items);
        // D-11：投影仅取摘要字段，SQL 不得包含 node_records 大列。
        Assert.DoesNotContain(_sqlLog, l => l.Contains("node_records", StringComparison.OrdinalIgnoreCase));
        // 返回的摘要包含必要字段。
        Assert.All(result.Items, s => Assert.False(string.IsNullOrEmpty(s.Status)));
    }

    [Fact]
    public async Task KeysetPaging_ReturnsOnlyOlderThanCursor_OrderedDesc()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = SeedWorkflow(out var records);
        await _dbContext.SaveChangesAsync(ct);

        // records[0] 最新，records[4] 最旧（StartedAt 递减）。
        var cursor = records[1].StartedAt; // 第二新

        var page = await _service.GetByWorkflowAsync(workflow.Id, beforeStartedAt: cursor, cancellationToken: ct);

        // 严格小于 cursor：仅 records[2..4]（3 条），且按 StartedAt 降序。
        var items = page.Items.ToList();
        Assert.Equal(3, items.Count);
        Assert.True(items[0].StartedAt < cursor);
        Assert.True(items[1].StartedAt < items[0].StartedAt);
        Assert.True(items[2].StartedAt < items[1].StartedAt);
    }

    [Fact]
    public async Task KeysetPaging_CursorOlderThanAll_ReturnsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = SeedWorkflow(out var records);
        await _dbContext.SaveChangesAsync(ct);

        // cursor 比最旧记录还旧：严格小于 (StartedAt < cursor) 不匹配任何记录，故返回空。
        var cursor = records[4].StartedAt.AddMinutes(-1);

        var page = await _service.GetByWorkflowAsync(workflow.Id, beforeStartedAt: cursor, cancellationToken: ct);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task KeysetPaging_CursorEqualToOldest_ReturnsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = SeedWorkflow(out var records);
        await _dbContext.SaveChangesAsync(ct);

        var page = await _service.GetByWorkflowAsync(workflow.Id, beforeStartedAt: records[4].StartedAt, cancellationToken: ct);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task GetByWorkflowAsync_WithoutCursor_StillWorks_BackwardCompatible()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = SeedWorkflow(out var records);
        await _dbContext.SaveChangesAsync(ct);

        var page = await _service.GetByWorkflowAsync(workflow.Id, page: 1, pageSize: 20, cancellationToken: ct);
        Assert.Equal(5, page.Items.Count);
        Assert.Equal(5, page.TotalCount);
    }

    private Workflow SeedWorkflow(out List<ExecutionRecord> records)
    {
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "Keyset Workflow",
            ProjectId = Guid.NewGuid(),
            Nodes = [],
            Connections = [],
            CreatedBy = "test",
            Version = 1,
            IsActive = true,
        };
        _dbContext.Workflows.Add(workflow);

        records = [];
        for (var i = 0; i < 5; i++)
        {
            records.Add(new ExecutionRecord
            {
                Id = Guid.NewGuid(),
                WorkflowDefinitionId = workflow.Id,
                Status = ExecutionStatus.Completed,
                StartedAt = DateTime.UtcNow.AddMinutes(-i),
                CompletedAt = DateTime.UtcNow.AddMinutes(-i),
                NodeRecords = [],
            });
        }

        _dbContext.ExecutionRecords.AddRange(records);
        return workflow;
    }

    private sealed class CapturingEngine : IEngine
    {
        public Task<ExecutionId> StartAsync(Guid workflowDefinitionId, object? triggerPayload = null, CancellationToken cancellationToken = default)
            => Task.FromResult(ExecutionId.From(Guid.NewGuid()));

        public Task<ExecutionId> StartAsync(Guid workflowDefinitionId, Workflow preloadedWorkflow, object? triggerPayload = null, CancellationToken cancellationToken = default)
            => StartAsync(workflowDefinitionId, triggerPayload, cancellationToken);
    }

    private sealed class StubIdempotencyService : IExecutionIdempotencyService
    {
        public Task<Guid?> TryGetOrRegisterAsync(string idempotencyKey, Guid executionId, TimeSpan? ttl = null, CancellationToken ct = default)
            => Task.FromResult<Guid?>(null);
        public Task<Guid?> TryGetExistingAsync(string idempotencyKey, CancellationToken ct = default)
            => Task.FromResult<Guid?>(null);
        public Task CleanupExpiredAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class StubResourceAuthorizationService(IUserContext userContext) : IResourceAuthorizationService
    {
        public Task<bool> CanAccessWorkflowAsync(Guid userId, Guid workflowId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(IsAllowed(operation));
        public Task<bool> CanAccessCredentialAsync(Guid userId, Guid credentialId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(IsAllowed(operation));
        public Task<bool> CanAccessExecutionAsync(Guid userId, Guid executionId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(IsAllowed(operation));
        public Task<bool> CanAccessTriggerAsync(Guid userId, Guid triggerId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(IsAllowed(operation));
        public bool ShouldMaskCredentialValues(IReadOnlyList<string> roles) => false;

        private bool IsAllowed(Operation operation)
        {
            var roles = userContext.Roles;
            return operation switch
            {
                Operation.Read => roles.Contains(RoleConstants.Admin) || roles.Contains(RoleConstants.Editor) || roles.Contains(RoleConstants.Viewer),
                Operation.Write => roles.Contains(RoleConstants.Admin) || roles.Contains(RoleConstants.Editor),
                Operation.Delete or Operation.Execute => roles.Contains(RoleConstants.Admin),
                _ => false,
            };
        }
    }
}
