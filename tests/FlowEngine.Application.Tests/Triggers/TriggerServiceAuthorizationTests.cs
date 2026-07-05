using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Triggers;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Tests.Triggers;

public sealed class TriggerServiceAuthorizationTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly FakeUserContext _userContext;
    private readonly TriggerService _service;

    public TriggerServiceAuthorizationTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _userContext = new FakeUserContext();

        var eventBus = new InMemoryEventBus();
        var auditFactory = new AuditEventFactory(_userContext);
        var scheduleManager = new FakeScheduleManager();
        var resourceAuthorization = new RoleBasedResourceAuthorizationService(_userContext);
        _service = new TriggerService(_dbContext, eventBus, auditFactory, scheduleManager, _userContext, resourceAuthorization, new WebhookRouteService(_dbContext));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetByIdAsync_UnauthenticatedUser_ThrowsPermissionDeniedException()
    {
        var ct = TestContext.Current.CancellationToken;
        _userContext.UserId = null;

        await Assert.ThrowsAsync<PermissionDeniedException>(() => _service.GetByIdAsync(Guid.NewGuid(), ct));
    }

    [Fact]
    public async Task GetByIdAsync_Viewer_CanReadExistingTrigger()
    {
        var ct = TestContext.Current.CancellationToken;
        var trigger = CreateTestTrigger();
        _dbContext.Triggers.Add(trigger);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Viewer];

        var result = await _service.GetByIdAsync(trigger.Id, ct);

        Assert.NotNull(result);
        Assert.Equal(trigger.Id, result.Id);
    }

    [Fact]
    public async Task UpdateAsync_Viewer_ThrowsPermissionDeniedException()
    {
        var ct = TestContext.Current.CancellationToken;
        var trigger = CreateTestTrigger();
        _dbContext.Triggers.Add(trigger);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Viewer];

        var dto = new UpdateTriggerDto
        {
            Name = "Updated",
            IsActive = false,
        };

        await Assert.ThrowsAsync<PermissionDeniedException>(() => _service.UpdateAsync(trigger.Id, dto, ct));
    }

    [Fact]
    public async Task UpdateAsync_Editor_UpdatesTrigger()
    {
        var ct = TestContext.Current.CancellationToken;
        var trigger = CreateTestTrigger();
        _dbContext.Triggers.Add(trigger);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Editor];

        var dto = new UpdateTriggerDto
        {
            Name = "Updated",
            IsActive = false,
        };

        var result = await _service.UpdateAsync(trigger.Id, dto, ct);

        Assert.NotNull(result);
        Assert.Equal("Updated", result.Name);
        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task DeleteAsync_Viewer_ThrowsPermissionDeniedException()
    {
        var ct = TestContext.Current.CancellationToken;
        var trigger = CreateTestTrigger();
        _dbContext.Triggers.Add(trigger);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Viewer];

        await Assert.ThrowsAsync<PermissionDeniedException>(() => _service.DeleteAsync(trigger.Id, ct));
    }

    [Fact]
    public async Task DeleteAsync_Admin_DeletesTrigger()
    {
        var ct = TestContext.Current.CancellationToken;
        var trigger = CreateTestTrigger();
        _dbContext.Triggers.Add(trigger);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Admin];

        var result = await _service.DeleteAsync(trigger.Id, ct);

        Assert.True(result);
        var deleted = await _dbContext.Triggers.FindAsync([trigger.Id], ct);
        Assert.Null(deleted);
    }

    private static Trigger CreateTestTrigger()
    {
        return new Trigger
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = Guid.NewGuid(),
            WorkflowVersion = 1,
            Type = TriggerType.Schedule,
            Name = "Test Trigger",
            IsActive = true,
            Settings = new TriggerSettings(),
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
