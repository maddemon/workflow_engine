using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Triggers;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Application.Tests.Workflows;

public sealed class WorkflowServiceAuthorizationTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly FakeUserContext _userContext;
    private readonly WorkflowService _service;
    private readonly InMemoryEventBus _eventBus;

    public WorkflowServiceAuthorizationTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _userContext = new FakeUserContext();
        _eventBus = new InMemoryEventBus();
        var auditFactory = new AuditEventFactory(_userContext);
        var scheduleManager = new FakeScheduleManager();
        var resourceAuthorization = new RoleBasedResourceAuthorizationService(_userContext);
        var authGuard = AuthorizationGuardFactory.Create(_userContext, resourceAuthorization, _eventBus);
        var triggerService = new TriggerService(_dbContext, _eventBus, auditFactory, scheduleManager, authGuard, new WebhookRouteService(_dbContext), NullLogger<TriggerService>.Instance);
        var validator = new WorkflowValidator(new FakeNodeRegistry());
        var handler = new AuthorizedOperationHandler(authGuard, _eventBus, auditFactory);
        var statisticsLoader = new WorkflowStatisticsLoader(_dbContext);
        var triggerSync = new WorkflowTriggerSync(triggerService, handler);
        _service = new WorkflowService(_dbContext, validator, _eventBus, auditFactory, triggerService, authGuard, handler, statisticsLoader, triggerSync, NullLogger<WorkflowService>.Instance);
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
    public async Task GetAsync_Viewer_CanReadExistingWorkflow()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = CreateTestWorkflow();
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Viewer];

        var result = await _service.GetAsync(workflow.Id, ct);

        Assert.NotNull(result);
        Assert.Equal(workflow.Id, result.Id);
    }

    [Fact]
    public async Task UpdateAsync_Viewer_ThrowsPermissionDeniedException()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = CreateTestWorkflow();
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Viewer];

        var dto = new UpdateWorkflowDto
        {
            Name = "Updated",
            IsActive = true,
            Nodes = [],
            Connections = [],
        };

        await Assert.ThrowsAsync<PermissionDeniedException>(() => _service.UpdateAsync(workflow.Id, dto, ct));

        var deniedEvent = _eventBus.PublishedEvents
            .OfType<AuditLogEvent>()
            .FirstOrDefault(e => e.EventType == AuditEventTypes.PermissionDenied);
        Assert.NotNull(deniedEvent);
        Assert.Equal(workflow.Id, deniedEvent.ResourceId);
        Assert.NotNull(deniedEvent.Payload);
        Assert.Equal("Write", deniedEvent.Payload!["operation"].ToString());
    }

    [Fact]
    public async Task UpdateAsync_Editor_UpdatesWorkflow()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = CreateTestWorkflow();
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Editor];

        var dto = new UpdateWorkflowDto
        {
            Name = "Updated",
            IsActive = true,
            Nodes = [],
            Connections = [],
        };

        var result = await _service.UpdateAsync(workflow.Id, dto, ct);

        Assert.NotNull(result);
        Assert.Equal("Updated", result.Name);
    }

    [Fact]
    public async Task DeleteAsync_Viewer_ThrowsPermissionDeniedException()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = CreateTestWorkflow();
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Viewer];

        await Assert.ThrowsAsync<PermissionDeniedException>(() => _service.DeleteAsync(workflow.Id, ct));

        var deniedEvent = _eventBus.PublishedEvents
            .OfType<AuditLogEvent>()
            .FirstOrDefault(e => e.EventType == AuditEventTypes.PermissionDenied);
        Assert.NotNull(deniedEvent);
        Assert.Equal(workflow.Id, deniedEvent.ResourceId);
        Assert.NotNull(deniedEvent.Payload);
        Assert.Equal("Delete", deniedEvent.Payload!["operation"].ToString());
    }

    [Fact]
    public async Task DeleteAsync_Admin_DeletesWorkflow()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = CreateTestWorkflow();
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Admin];

        var result = await _service.DeleteAsync(workflow.Id, ct);

        Assert.True(result);
        var deleted = await _dbContext.Workflows.FindAsync([workflow.Id], ct);
        Assert.Null(deleted);
    }

    private static Workflow CreateTestWorkflow()
    {
        return new Workflow
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Name = "Test Workflow",
            CreatedBy = "test-user",
            IsActive = true,
            Nodes = [],
            Connections = [],
        };
    }

    private sealed class FakeUserContext : IUserContext
    {
        public bool IsAuthenticated => UserId.HasValue;
        public Guid? UserId { get; set; } = Guid.NewGuid();
        public string? Email => "test@test.com";
        public IReadOnlyList<string> Roles { get; set; } = [];
    }

    private sealed class FakeNodeRegistry : INodeRegistry
    {
        public void Register(INodeType nodeType) { }
        public INodeType Get(string typeName) => throw new InvalidOperationException();
        public bool TryGet(string typeName, out INodeType? nodeType) { nodeType = null; return false; }
        public IReadOnlyCollection<INodeType> GetAll() => [];
        public INodeType CreateInstance(string typeName) => throw new InvalidOperationException();
        public IReadOnlyCollection<NodeTypeDescriptor> GetDescriptors() => [];
        public NodeTypeDescriptor GetDescriptor(string typeName) => throw new InvalidOperationException();
    }

    private sealed class InMemoryEventBus : IEventBus
    {
        public List<object> PublishedEvents { get; } = [];

        public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
        {
            PublishedEvents.Add(eventInstance!);
            return Task.CompletedTask;
        }
        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
            where TEvent : IDomainEvent => new Disposable();
        private sealed class Disposable : IDisposable { public void Dispose() { } }
    }

    private sealed class FakeScheduleManager : IScheduleManager
    {
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RegisterScheduleAsync(Guid triggerId, Guid workflowDefinitionId, string cronExpression, string? timeZone, DateTime? startAt, DateTime? endAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UnregisterScheduleAsync(Guid triggerId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DateTime?> GetNextFireTimeAsync(Guid triggerId, CancellationToken cancellationToken = default) => Task.FromResult<DateTime?>(null);
        public Task RegisterPollTriggerAsync(Guid triggerId, Guid workflowDefinitionId, int intervalSeconds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UnregisterPollTriggerAsync(Guid triggerId, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
