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

public sealed class ExecutionServiceAuthorizationTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly FakeUserContext _userContext;
    private readonly ExecutionService _service;
    private readonly RecordingEventBus _eventBus;

    public ExecutionServiceAuthorizationTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _userContext = new FakeUserContext();

        var resourceAuthorization = new RoleBasedResourceAuthorizationService(_userContext);
        var engine = new StubEngine();
        var idempotencyService = new StubIdempotencyService();
        _eventBus = new RecordingEventBus();
        var auditFactory = new AuditEventFactory(_userContext);
        _service = new ExecutionService(engine, _dbContext, idempotencyService, AuthorizationGuardFactory.Create(_userContext, resourceAuthorization, _eventBus), _eventBus, auditFactory, new ExecutionCancellationRegistry());
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetAsync_UnauthenticatedUser_ThrowsUnauthorizedException()
    {
        var ct = TestContext.Current.CancellationToken;
        _userContext.UserId = null;

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.GetAsync(Guid.NewGuid(), ct));
    }

    [Fact]
    public async Task GetAsync_UnauthorizedRole_ThrowsPermissionDeniedException()
    {
        var ct = TestContext.Current.CancellationToken;
        var execution = CreateTestExecution();
        _dbContext.ExecutionRecords.Add(execution);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = []; // 没有任何角色

        await Assert.ThrowsAsync<PermissionDeniedException>(() => _service.GetAsync(execution.Id, ct));

        var deniedEvent = _eventBus.PublishedEvents
            .OfType<AuditLogEvent>()
            .FirstOrDefault(e => e.EventType == AuditEventTypes.PermissionDenied);
        Assert.NotNull(deniedEvent);
        Assert.Equal(execution.Id, deniedEvent.ResourceId);
        Assert.NotNull(deniedEvent.Payload);
        Assert.Equal("Read", deniedEvent.Payload!["operation"].ToString());
    }

    [Fact]
    public async Task GetAsync_Viewer_CanReadExistingExecution()
    {
        var ct = TestContext.Current.CancellationToken;
        var execution = CreateTestExecution();
        _dbContext.ExecutionRecords.Add(execution);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Viewer];

        var result = await _service.GetAsync(execution.Id, ct);

        Assert.NotNull(result);
        Assert.Equal(execution.Id, result.Id);
    }

    [Fact]
    public async Task GetAsync_Admin_CanReadExistingExecution()
    {
        var ct = TestContext.Current.CancellationToken;
        var execution = CreateTestExecution();
        _dbContext.ExecutionRecords.Add(execution);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Admin];

        var result = await _service.GetAsync(execution.Id, ct);

        Assert.NotNull(result);
        Assert.Equal(execution.Id, result.Id);
    }

    [Fact]
    public async Task ExecuteAsync_UnauthenticatedUser_ThrowsUnauthorizedException()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = CreateTestWorkflow();
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.UserId = null;

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.ExecuteAsync(workflow.Id, cancellationToken: ct));
    }

    [Fact]
    public async Task ExecuteAsync_UnauthorizedRole_ThrowsPermissionDeniedException()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = CreateTestWorkflow();
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Viewer];

        await Assert.ThrowsAsync<PermissionDeniedException>(() => _service.ExecuteAsync(workflow.Id, cancellationToken: ct));

        var deniedEvent = _eventBus.PublishedEvents
            .OfType<AuditLogEvent>()
            .FirstOrDefault(e => e.EventType == AuditEventTypes.PermissionDenied);
        Assert.NotNull(deniedEvent);
        Assert.Equal(workflow.Id, deniedEvent.ResourceId);
        Assert.NotNull(deniedEvent.Payload);
        Assert.Equal("Execute", deniedEvent.Payload!["operation"].ToString());
    }

    [Fact]
    public async Task ExecuteAsync_Admin_CanStartExecution()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = CreateTestWorkflow();
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Admin];

        var result = await _service.ExecuteAsync(workflow.Id, cancellationToken: ct);

        Assert.NotNull(result);
        Assert.Equal(workflow.Id, result.WorkflowDefinitionId);
    }

    [Fact]
    public async Task ExecuteAsync_Editor_CanStartExecution()
    {
        // 验证 Editor 在 Workflow scope 上拥有 Execute 权限（对齐真实 PermissionMapping 策略）。
        var ct = TestContext.Current.CancellationToken;
        var workflow = CreateTestWorkflow();
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Editor];

        var result = await _service.ExecuteAsync(workflow.Id, cancellationToken: ct);

        Assert.NotNull(result);
        Assert.Equal(workflow.Id, result.WorkflowDefinitionId);
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

    private static ExecutionRecord CreateTestExecution()
    {
        return new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = Guid.NewGuid(),
            Status = ExecutionStatus.Completed,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            NodeRecords = [],
        };
    }

    private sealed class StubEngine : IEngine
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

}
