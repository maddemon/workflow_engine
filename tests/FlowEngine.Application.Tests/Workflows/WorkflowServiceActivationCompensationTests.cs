using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Tests.TestSupport.Fakes;
using FlowEngine.Application.Triggers;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Application.Tests.Workflows;

/// <summary>
/// D-10/EX-3：WorkflowService.UpdateAsync 在激活工作流时，先提交数据库写入（释放行锁），
/// 再调用外部调度器；若调度注册失败，则补偿回退为非激活并告警，杜绝“已激活但调度未注册”的静默失效。
/// </summary>
public sealed class WorkflowServiceActivationCompensationTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly FakeUserContext _userContext;
    private readonly RecordingEventBus _eventBus;
    private readonly WorkflowService _service;
    private readonly TriggerService _triggerService;

    public WorkflowServiceActivationCompensationTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _eventBus = new RecordingEventBus();
        _userContext = new FakeUserContext { Roles = [RoleConstants.Admin] };
        var auditFactory = new AuditEventFactory(_userContext);
        var resourceAuthorization = new RoleBasedResourceAuthorizationService(_userContext);
        var authGuard = AuthorizationGuardFactory.Create(_userContext, resourceAuthorization, _eventBus);
        // 调度注册失败模拟器：用于触发 D-10/EX-3 补偿路径。
        var scheduleManager = new ThrowingScheduleManager();
        _triggerService = new TriggerService(
            _dbContext, _eventBus, auditFactory, scheduleManager, authGuard, new WebhookRouteService(_dbContext), NullLogger<TriggerService>.Instance);
        var validator = new WorkflowValidator(new FakeNodeRegistry());
        var handler = new AuthorizedOperationHandler(authGuard, _eventBus, auditFactory);
        var statisticsLoader = new WorkflowStatisticsLoader(_dbContext);
        var triggerSync = new WorkflowTriggerSync(_triggerService, handler);
        _service = new WorkflowService(
            _dbContext, validator, _eventBus, auditFactory, _triggerService, authGuard, handler, statisticsLoader, triggerSync, NullLogger<WorkflowService>.Instance);
    }

    public void Dispose() => _dbContext.Dispose();

    private async Task<Guid> SeedDraftWithScheduleTriggerAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var draft = await _service.CreateDraftAsync(new CreateWorkflowDto
        {
            Name = "draft",
            CreatedBy = "tester",
            Nodes = [],
            Connections = [],
        }, ct);

        // 挂一个 Schedule 触发器（激活时由 WorkflowTriggerSync 注册 → 触发失败模拟器）。
        await _triggerService.CreateAsync(new CreateTriggerDto
        {
            WorkflowDefinitionId = draft.Id,
            Type = TriggerType.Schedule,
            Name = "sched",
            IsActive = true,
            Settings = new TriggerSettingsDto { CronExpression = "0 0 * * *" },
        }, ct);

        return draft.Id;
    }

    [Fact]
    public async Task UpdateAsync_Activation_SchedulerFails_CompensatesToInactive()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = await SeedDraftWithScheduleTriggerAsync();

        // 激活：调度注册失败不应抛异常，工作流应回退为非激活（D-10 提交在前、EX-3 补偿在后）。
        await _service.UpdateAsync(workflowId, new UpdateWorkflowDto
        {
            Name = "draft",
            IsActive = true,
            Nodes = [],
            Connections = [],
        }, ct);

        var persisted = await _dbContext.Workflows.FirstOrDefaultAsync(w => w.Id == workflowId, ct);
        Assert.NotNull(persisted);
        Assert.False(persisted!.IsActive); // 补偿回退，状态与调度实际一致。
    }

    [Fact]
    public async Task UpdateAsync_Activation_SchedulerSucceeds_StaysActive()
    {
        var ct = TestContext.Current.CancellationToken;
        // 用成功调度器重建服务，验证正常路径。
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        await using var ctx2 = new FlowEngineDbContext(options);
        var eventBus2 = new RecordingEventBus();
        var userContext2 = new FakeUserContext { Roles = [RoleConstants.Admin] };
        var auditFactory2 = new AuditEventFactory(userContext2);
        var ra2 = new RoleBasedResourceAuthorizationService(userContext2);
        var guard2 = AuthorizationGuardFactory.Create(userContext2, ra2, eventBus2);
        var triggerSvc2 = new TriggerService(ctx2, eventBus2, auditFactory2, new FakeScheduleManager(), guard2, new WebhookRouteService(ctx2), NullLogger<TriggerService>.Instance);
        var validator2 = new WorkflowValidator(new FakeNodeRegistry());
        var handler2 = new AuthorizedOperationHandler(guard2, eventBus2, auditFactory2);
        var loader2 = new WorkflowStatisticsLoader(ctx2);
        var sync2 = new WorkflowTriggerSync(triggerSvc2, handler2);
        var service2 = new WorkflowService(ctx2, validator2, eventBus2, auditFactory2, triggerSvc2, guard2, handler2, loader2, sync2, NullLogger<WorkflowService>.Instance);

        var draft = await service2.CreateDraftAsync(new CreateWorkflowDto { Name = "draft", CreatedBy = "tester", Nodes = [], Connections = [] }, ct);
        await triggerSvc2.CreateAsync(new CreateTriggerDto { WorkflowDefinitionId = draft.Id, Type = TriggerType.Schedule, Name = "sched", IsActive = true, Settings = new TriggerSettingsDto { CronExpression = "0 0 * * *" } }, ct);

        await service2.UpdateAsync(draft.Id, new UpdateWorkflowDto { Name = "draft", IsActive = true, Nodes = [], Connections = [] }, ct);

        var persisted = await ctx2.Workflows.FirstOrDefaultAsync(w => w.Id == draft.Id, ct);
        Assert.NotNull(persisted);
        Assert.True(persisted!.IsActive);
    }

    private sealed class ThrowingScheduleManager : IScheduleManager
    {
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RegisterScheduleAsync(Guid triggerId, Guid workflowDefinitionId, string cronExpression, string? timeZone = null, DateTime? startAt = null, DateTime? endAt = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("simulated scheduler failure");
        public Task UnregisterScheduleAsync(Guid triggerId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DateTime?> GetNextFireTimeAsync(Guid triggerId, CancellationToken cancellationToken = default)
            => Task.FromResult<DateTime?>(null);
        public Task RegisterPollTriggerAsync(Guid triggerId, Guid workflowDefinitionId, int intervalSeconds, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("simulated scheduler failure");
        public Task UnregisterPollTriggerAsync(Guid triggerId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
