using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Triggers;
using FlowEngine.Application.Workflows;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Application.Tests.TestSupport.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Application.Tests.Workflows;

/// <summary>
/// 直接覆盖 <see cref="WorkflowTriggerSync.SyncActivationAsync"/> 的触发器同步分支：
/// 激活翻转应注册调度并发布 WorkflowActivated 审计事件；停用翻转应注销调度并发布
/// WorkflowDeactivated；未发生状态翻转时不应产生任何副作用。此前该分支仅经
/// WorkflowService.UpdateAsync 间接覆盖，本测试锁定 SyncActivationAsync 的直接行为。
/// </summary>
public sealed class WorkflowTriggerSyncTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly RecordingEventBus _eventBus;
    private readonly RecordingScheduleManager _scheduleManager;
    private readonly WorkflowTriggerSync _sync;

    public WorkflowTriggerSyncTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _eventBus = new RecordingEventBus();
        var userContext = new FakeUserContext();
        var auditFactory = new AuditEventFactory(userContext);
        _scheduleManager = new RecordingScheduleManager();
        var resourceAuthorization = new RoleBasedResourceAuthorizationService(userContext);
        var authGuard = AuthorizationGuardFactory.Create(userContext, resourceAuthorization, _eventBus);
        var triggerService = new TriggerService(
            _dbContext, _eventBus, auditFactory, _scheduleManager, authGuard,
            new WebhookRouteService(_dbContext), NullLogger<TriggerService>.Instance);
        var handler = new AuthorizedOperationHandler(authGuard, _eventBus, auditFactory);
        _sync = new WorkflowTriggerSync(triggerService, handler);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task SyncActivationAsync_ActivateTransition_RegistersScheduleAndPublishesActivated()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = SeedWorkflowWithScheduleTrigger("ToActivate", out var triggerId);

        await _sync.SyncActivationAsync(workflow, previousIsActive: false, currentIsActive: true, ct);

        // 注册应委派到调度管理器（按工作流定义 ID）。
        Assert.Contains(_scheduleManager.RegisteredWorkflowIds, id => id == workflow.Id);
        // 审计事件应发布 WorkflowActivated。
        Assert.Contains(_eventBus.PublishedEvents,
            e => e is AuditLogEvent a && a.EventType == AuditEventTypes.WorkflowActivated);
    }

    [Fact]
    public async Task SyncActivationAsync_DeactivateTransition_UnregistersScheduleAndPublishesDeactivated()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = SeedWorkflowWithScheduleTrigger("ToDeactivate", out var triggerId);

        await _sync.SyncActivationAsync(workflow, previousIsActive: true, currentIsActive: false, ct);

        // 注销应委派到调度管理器（按触发器 ID）。
        Assert.Contains(_scheduleManager.UnregisteredTriggerIds, id => id == triggerId);
        // 审计事件应发布 WorkflowDeactivated。
        Assert.Contains(_eventBus.PublishedEvents,
            e => e is AuditLogEvent a && a.EventType == AuditEventTypes.WorkflowDeactivated);
    }

    [Fact]
    public async Task SyncActivationAsync_NoFlip_NoSideEffects()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = SeedWorkflowWithScheduleTrigger("NoFlip", out _);

        // 激活→激活、停用→停用 两种无翻转情形都不应触碰调度或发布翻转审计事件。
        await _sync.SyncActivationAsync(workflow, previousIsActive: true, currentIsActive: true, ct);
        await _sync.SyncActivationAsync(workflow, previousIsActive: false, currentIsActive: false, ct);

        Assert.Empty(_scheduleManager.RegisteredWorkflowIds);
        Assert.Empty(_scheduleManager.UnregisteredTriggerIds);
        Assert.DoesNotContain(_eventBus.PublishedEvents,
            e => e is AuditLogEvent a &&
                 (a.EventType == AuditEventTypes.WorkflowActivated ||
                  a.EventType == AuditEventTypes.WorkflowDeactivated));
    }

    private Workflow SeedWorkflowWithScheduleTrigger(string name, out Guid triggerId)
    {
        var workflowId = Guid.NewGuid();
        var workflow = new Workflow
        {
            Id = workflowId,
            Name = name,
            CreatedBy = "tester",
            IsActive = true,
            Nodes = [new NodeDefinition { Id = "n1", TypeName = "fetch", Name = "Fetch" }],
            Connections = [],
        };
        _dbContext.Workflows.Add(workflow);

        var trigger = new Trigger
        {
            Id = Guid.NewGuid(),
            Name = "schedule",
            WorkflowDefinitionId = workflowId,
            ProjectId = Guid.NewGuid(),
            Type = TriggerType.Schedule,
            IsActive = true,
            Settings = new TriggerSettings { CronExpression = "0 0 * * *" },
        };
        _dbContext.Triggers.Add(trigger);
        _dbContext.SaveChanges();

        triggerId = trigger.Id;
        return workflow;
    }

    private sealed class RecordingScheduleManager : IScheduleManager
    {
        public List<Guid> RegisteredWorkflowIds { get; } = [];
        public List<Guid> UnregisteredTriggerIds { get; } = [];

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RegisterScheduleAsync(Guid triggerId, Guid workflowDefinitionId, string cronExpression, string? timeZone = null, DateTime? startAt = null, DateTime? endAt = null, CancellationToken cancellationToken = default)
        {
            RegisteredWorkflowIds.Add(workflowDefinitionId);
            return Task.CompletedTask;
        }
        public Task UnregisterScheduleAsync(Guid triggerId, CancellationToken cancellationToken = default)
        {
            UnregisteredTriggerIds.Add(triggerId);
            return Task.CompletedTask;
        }
        public Task<DateTime?> GetNextFireTimeAsync(Guid triggerId, CancellationToken cancellationToken = default)
            => Task.FromResult<DateTime?>(null);
        public Task RegisterPollTriggerAsync(Guid triggerId, Guid workflowDefinitionId, int intervalSeconds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UnregisterPollTriggerAsync(Guid triggerId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
