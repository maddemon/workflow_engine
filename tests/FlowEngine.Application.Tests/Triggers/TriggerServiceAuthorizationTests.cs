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
using FlowEngine.Application.Tests.TestSupport.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Application.Tests.Triggers;

public sealed class TriggerServiceAuthorizationTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly FakeUserContext _userContext;
    private readonly TriggerService _service;
    private readonly RecordingEventBus _eventBus;

    public TriggerServiceAuthorizationTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _userContext = new FakeUserContext();
        _eventBus = new RecordingEventBus();
        var auditFactory = new AuditEventFactory(_userContext);
        var scheduleManager = new FakeScheduleManager();
        var resourceAuthorization = new RoleBasedResourceAuthorizationService(_userContext);
        _service = new TriggerService(_dbContext, _eventBus, auditFactory, scheduleManager, AuthorizationGuardFactory.Create(_userContext, resourceAuthorization, _eventBus), new WebhookRouteService(_dbContext), NullLogger<TriggerService>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetByIdAsync_UnauthenticatedUser_ThrowsUnauthorizedException()
    {
        var ct = TestContext.Current.CancellationToken;
        _userContext.UserId = null;

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.GetByIdAsync(Guid.NewGuid(), ct));
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

        var deniedEvent = _eventBus.PublishedEvents
            .OfType<AuditLogEvent>()
            .FirstOrDefault(e => e.EventType == AuditEventTypes.PermissionDenied);
        Assert.NotNull(deniedEvent);
        Assert.Equal(trigger.Id, deniedEvent.ResourceId);
        Assert.NotNull(deniedEvent.Payload);
        Assert.Equal("Write", deniedEvent.Payload!["operation"].ToString());
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

        var deniedEvent = _eventBus.PublishedEvents
            .OfType<AuditLogEvent>()
            .FirstOrDefault(e => e.EventType == AuditEventTypes.PermissionDenied);
        Assert.NotNull(deniedEvent);
        Assert.Equal(trigger.Id, deniedEvent.ResourceId);
        Assert.NotNull(deniedEvent.Payload);
        Assert.Equal("Delete", deniedEvent.Payload!["operation"].ToString());
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

}
