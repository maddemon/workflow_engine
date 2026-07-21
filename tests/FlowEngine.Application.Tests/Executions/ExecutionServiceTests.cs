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
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.Executor;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Tests.Executions;

public sealed class ExecutionServiceTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly FakeUserContext _userContext;
    private readonly CapturingEngine _engine;
    private readonly RecordingEventBus _eventBus;
    private readonly ExecutionCancellationRegistry _cancellationRegistry;
    private readonly ExecutionService _service;

    public ExecutionServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _userContext = new FakeUserContext();
        _userContext.Roles = [RoleConstants.Admin];
        _engine = new CapturingEngine();
        _eventBus = new RecordingEventBus();
        var auditFactory = new AuditEventFactory(_userContext);
        var resourceAuthorization = new StubResourceAuthorizationService(_userContext);
        var idempotencyService = new StubIdempotencyService();
        _cancellationRegistry = new ExecutionCancellationRegistry();
        _service = new ExecutionService(
            _engine,
            _dbContext,
            idempotencyService,
            AuthorizationGuardFactory.Create(_userContext, resourceAuthorization),
            _eventBus,
            auditFactory,
            _cancellationRegistry);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_WithInputs_PassesInputsToEngine()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = CreateTestWorkflow();
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(ct);

        var inputs = new Dictionary<string, object> { ["greeting"] = "hello" };

        await _service.ExecuteAsync(workflow.Id, inputs: inputs, cancellationToken: ct);

        Assert.NotNull(_engine.LastPayload);
        Assert.Equal(inputs, _engine.LastPayload);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutInputs_PassesNullToEngine()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = CreateTestWorkflow();
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(ct);

        await _service.ExecuteAsync(workflow.Id, cancellationToken: ct);

        Assert.Null(_engine.LastPayload);
    }

    [Fact]
    public async Task ExecuteAsync_PublishesExecutionStartedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = CreateTestWorkflow();
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(ct);

        var result = await _service.ExecuteAsync(workflow.Id, cancellationToken: ct);

        Assert.NotNull(result);
        var startedEvent = _eventBus.PublishedEvents
            .OfType<AuditLogEvent>()
            .FirstOrDefault(e => e.EventType == AuditEventTypes.ExecutionStarted);
        Assert.NotNull(startedEvent);
        Assert.Equal(result!.Id, startedEvent.ResourceId);
        Assert.Equal(workflow.Id, startedEvent.Payload!["workflowDefinitionId"]);
    }

    [Fact]
    public async Task ExecuteAsync_NonExistingWorkflow_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _service.ExecuteAsync(Guid.NewGuid(), cancellationToken: ct);
        Assert.Null(result);
    }

    [Fact]
    public async Task CancelAsync_PendingExecution_SetsCancelledAndPublishesEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var execution = CreateTestExecution(ExecutionStatus.Pending);
        _dbContext.ExecutionRecords.Add(execution);
        await _dbContext.SaveChangesAsync(ct);

        var (result, conflict) = await _service.CancelAsync(execution.Id, ct);

        Assert.False(conflict);
        Assert.NotNull(result);
        Assert.Equal(nameof(ExecutionStatus.Cancelled), result!.Status);

        var record = await _dbContext.ExecutionRecords.FindAsync([execution.Id], ct);
        Assert.NotNull(record);
        Assert.Equal(ExecutionStatus.Cancelled, record.Status);
        Assert.NotNull(record.CompletedAt);

        var cancelledEvent = _eventBus.PublishedEvents
            .OfType<WorkflowCancelledEvent>()
            .FirstOrDefault();
        Assert.NotNull(cancelledEvent);
        Assert.Equal(execution.Id, cancelledEvent.ExecutionId);
        Assert.Equal(execution.WorkflowDefinitionId, cancelledEvent.WorkflowDefinitionId);
    }

    [Fact]
    public async Task CancelAsync_RunningExecution_SignalsCancellationRegistry()
    {
        var ct = TestContext.Current.CancellationToken;
        var execution = CreateTestExecution(ExecutionStatus.Running);
        _dbContext.ExecutionRecords.Add(execution);
        await _dbContext.SaveChangesAsync(ct);

        // 模拟后台 worker 已为该执行登记取消令牌源。
        using var workerCts = new CancellationTokenSource();
        _cancellationRegistry.Register(execution.Id, workerCts);

        var (result, conflict) = await _service.CancelAsync(execution.Id, ct);

        // 运行中执行：CancelAsync 仅信号注册表触发取消，不直接落库 Cancelled（由 worker 落库）。
        Assert.False(conflict);
        Assert.NotNull(result);
        Assert.True(workerCts.IsCancellationRequested, "运行中的执行应经注册表触发取消。");
        // worker 尚未处理，DB 中状态仍为 Running（取消为异步）。
        var record = await _dbContext.ExecutionRecords.FindAsync([execution.Id], ct);
        Assert.Equal(ExecutionStatus.Running, record!.Status);
    }

    [Fact]
    public async Task CancelAsync_PendingExecution_SetsCancelledAndSignalsRegistry()
    {
        var ct = TestContext.Current.CancellationToken;
        var execution = CreateTestExecution(ExecutionStatus.Pending);
        _dbContext.ExecutionRecords.Add(execution);
        await _dbContext.SaveChangesAsync(ct);

        using var workerCts = new CancellationTokenSource();
        _cancellationRegistry.Register(execution.Id, workerCts);

        var (result, conflict) = await _service.CancelAsync(execution.Id, ct);

        Assert.False(conflict);
        Assert.NotNull(result);
        Assert.Equal(nameof(ExecutionStatus.Cancelled), result!.Status);

        var record = await _dbContext.ExecutionRecords.FindAsync([execution.Id], ct);
        Assert.NotNull(record);
        Assert.Equal(ExecutionStatus.Cancelled, record.Status);
        Assert.NotNull(record.CompletedAt);

        // 未出队的 Pending 执行同样应触发取消信号（worker 取出后会跳过终态）。
        Assert.True(workerCts.IsCancellationRequested);

        var cancelledEvent = _eventBus.PublishedEvents
            .OfType<WorkflowCancelledEvent>()
            .FirstOrDefault();
        Assert.NotNull(cancelledEvent);
        Assert.Equal(execution.Id, cancelledEvent.ExecutionId);
        Assert.Equal(execution.WorkflowDefinitionId, cancelledEvent.WorkflowDefinitionId);
    }

    [Fact]
    public async Task CancelAsync_CompletedExecution_ReturnsConflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var execution = CreateTestExecution(ExecutionStatus.Completed);
        _dbContext.ExecutionRecords.Add(execution);
        await _dbContext.SaveChangesAsync(ct);

        var (result, conflict) = await _service.CancelAsync(execution.Id, ct);

        Assert.True(conflict);
        Assert.NotNull(result);
        Assert.Equal(nameof(ExecutionStatus.Completed), result!.Status);
    }

    [Fact]
    public async Task CancelAsync_CancelledExecution_ReturnsConflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var execution = CreateTestExecution(ExecutionStatus.Cancelled);
        _dbContext.ExecutionRecords.Add(execution);
        await _dbContext.SaveChangesAsync(ct);

        var (result, conflict) = await _service.CancelAsync(execution.Id, ct);

        Assert.True(conflict);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CancelAsync_NonExistingExecution_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var (result, conflict) = await _service.CancelAsync(Guid.NewGuid(), ct);
        Assert.Null(result);
        Assert.False(conflict);
    }

    [Fact]
    public async Task CancelAsync_UnauthenticatedUser_ThrowsUnauthorizedException()
    {
        var ct = TestContext.Current.CancellationToken;
        _userContext.UserId = null;

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.CancelAsync(Guid.NewGuid(), ct));
    }

    [Fact]
    public async Task CancelAsync_UnauthorizedRole_ThrowsPermissionDeniedException()
    {
        var ct = TestContext.Current.CancellationToken;
        var execution = CreateTestExecution(ExecutionStatus.Pending);
        _dbContext.ExecutionRecords.Add(execution);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Viewer];

        await Assert.ThrowsAsync<PermissionDeniedException>(() => _service.CancelAsync(execution.Id, ct));
    }

    private static Workflow CreateTestWorkflow()
    {
        return new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "Test Workflow",
            ProjectId = Guid.NewGuid(),
            Nodes = [],
            Connections = [],
            CreatedBy = "test",
            Version = 1,
            IsActive = true,
        };
    }

    private static ExecutionRecord CreateTestExecution(ExecutionStatus status)
    {
        return new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = Guid.NewGuid(),
            Status = status,
            StartedAt = DateTime.UtcNow,
            CompletedAt = status is ExecutionStatus.Completed or ExecutionStatus.Cancelled or ExecutionStatus.Failed ? DateTime.UtcNow : null,
            NodeRecords = [],
        };
    }

    private sealed class CapturingEngine : IEngine
    {
        public object? LastPayload { get; private set; }

        public Task<ExecutionId> StartAsync(Guid workflowDefinitionId, object? triggerPayload = null, CancellationToken cancellationToken = default)
        {
            LastPayload = triggerPayload;
            return Task.FromResult(ExecutionId.From(Guid.NewGuid()));
        }
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
