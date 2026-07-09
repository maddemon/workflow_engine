using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Executions;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Tests.Executions;

public sealed class ExecutionServiceAuthorizationTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly FakeUserContext _userContext;
    private readonly ExecutionService _service;

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
        var eventBus = new InMemoryEventBus();
        var auditFactory = new AuditEventFactory(_userContext);
        _service = new ExecutionService(engine, _dbContext, idempotencyService, AuthorizationGuardFactory.Create(_userContext, resourceAuthorization), eventBus, auditFactory);
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

    private sealed class FakeUserContext : IUserContext
    {
        public bool IsAuthenticated => UserId.HasValue;
        public Guid? UserId { get; set; } = Guid.NewGuid();
        public string? Email => "test@test.com";
        public IReadOnlyList<string> Roles { get; set; } = [];
    }

    private sealed class InMemoryEventBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent => Task.CompletedTask;
        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
            where TEvent : IDomainEvent => new Disposable();
        private sealed class Disposable : IDisposable { public void Dispose() { } }
    }

    private sealed class StubEngine : IEngine
    {
        public Task<ExecutionId> StartAsync(Guid workflowDefinitionId, object? triggerPayload = null, CancellationToken cancellationToken = default)
            => Task.FromResult(ExecutionId.From(Guid.NewGuid()));
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

    private sealed class RoleBasedResourceAuthorizationService(IUserContext userContext) : IResourceAuthorizationService
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
