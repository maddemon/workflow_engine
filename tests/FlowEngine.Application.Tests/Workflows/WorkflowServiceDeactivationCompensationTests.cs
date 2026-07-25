using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
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
/// GAP 2（D-10 / EX-3 对称性）：WorkflowService 停用工作流时，须先提交 DB 写入（释放行锁），
/// 再注销外部调度，并以 try/catch + 补偿与激活路径对称，杜绝“事务跨外部 await”与“停用但调度残留”。
/// </summary>
public sealed class WorkflowServiceDeactivationCompensationTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly RecordingEventBus _eventBus;
    private readonly FakeUserContext _userContext;
    private readonly DeactivationOrderFake _scheduleManager;
    private readonly TriggerService _triggerService;
    private readonly WorkflowService _service;

    public WorkflowServiceDeactivationCompensationTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _eventBus = new RecordingEventBus();
        _userContext = new FakeUserContext { Roles = [RoleConstants.Admin] };
        var auditFactory = new AuditEventFactory(_userContext);
        var authGuard = AuthorizationGuardFactory.Create(_userContext, new RoleBasedResourceAuthorizationService(_userContext), _eventBus);
        _scheduleManager = new DeactivationOrderFake(_dbContext);
        _triggerService = new TriggerService(
            _dbContext, _eventBus, auditFactory, _scheduleManager, authGuard, new WebhookRouteService(_dbContext), NullLogger<TriggerService>.Instance);
        var validator = new WorkflowValidator(new FakeNodeRegistry());
        var handler = new AuthorizedOperationHandler(authGuard, _eventBus, auditFactory);
        var statisticsLoader = new WorkflowStatisticsLoader(_dbContext);
        var triggerSync = new WorkflowTriggerSync(_triggerService, handler);
        _service = new WorkflowService(
            _dbContext, validator, _eventBus, auditFactory, _triggerService, authGuard, handler, statisticsLoader, triggerSync, NullLogger<WorkflowService>.Instance);
    }

    public void Dispose() => _dbContext.Dispose();

    private async Task<Guid> SeedActiveWorkflowWithScheduleTriggerAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = new Workflow
        {
            Name = "wf",
            IsActive = true,
            ProjectId = null,
            Nodes = [],
            Connections = [],
            CreatedBy = "tester",
        };
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(ct);

        await _triggerService.CreateAsync(new CreateTriggerDto
        {
            WorkflowDefinitionId = workflow.Id,
            Type = TriggerType.Schedule,
            Name = "sched",
            IsActive = true,
            Settings = new TriggerSettingsDto { CronExpression = "0 0 * * *" },
        }, ct);

        return workflow.Id;
    }

    [Fact]
    public async Task UpdateAsync_Deactivation_CommitsDbBeforeUnregister_AndStaysInactive()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = await SeedActiveWorkflowWithScheduleTriggerAsync();

        // 停用：外部注销应在 DB 提交（IsActive=false）之后才发生，且不抛异常、保持停用。
        await _service.UpdateAsync(workflowId, new UpdateWorkflowDto
        {
            Name = "wf",
            IsActive = false,
            Nodes = [],
            Connections = [],
        }, ct);

        Assert.True(_scheduleManager.UnregisterCalledAfterCommit,
            "外部注销应在 DB 提交（IsActive=false）之后才调用（D-10）。");
        var persisted = await _dbContext.Workflows.FirstAsync(w => w.Id == workflowId, ct);
        Assert.False(persisted.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_Deactivation_UnregisterFails_CompensatesToActive()
    {
        var ct = TestContext.Current.CancellationToken;
        _scheduleManager.ThrowOnUnregister = true;
        var workflowId = await SeedActiveWorkflowWithScheduleTriggerAsync();

        // 停用：注销失败时回退为激活，使 DB 状态与仍存活的调度一致（EX-3 对称补偿）。
        await _service.UpdateAsync(workflowId, new UpdateWorkflowDto
        {
            Name = "wf",
            IsActive = false,
            Nodes = [],
            Connections = [],
        }, ct);

        var persisted = await _dbContext.Workflows.FirstAsync(w => w.Id == workflowId, ct);
        Assert.True(persisted.IsActive);
    }

    private sealed class DeactivationOrderFake : IScheduleManager
    {
        private readonly FlowEngineDbContext _ctx;
        public bool UnregisterCalledAfterCommit { get; private set; }
        public bool ThrowOnUnregister { get; set; }

        public DeactivationOrderFake(FlowEngineDbContext ctx) => _ctx = ctx;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RegisterScheduleAsync(Guid triggerId, Guid workflowDefinitionId, string cronExpression, string? timeZone = null, DateTime? startAt = null, DateTime? endAt = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task UnregisterScheduleAsync(Guid triggerId, CancellationToken cancellationToken = default)
        {
            // 注销被调用时重新从 DB 加载工作流：应已被提交为非激活，证明 DB 提交先于外部注销。
            var reloaded = _ctx.Workflows.AsNoTracking().IgnoreQueryFilters().First();
            UnregisterCalledAfterCommit = !reloaded.IsActive;
            if (ThrowOnUnregister) throw new InvalidOperationException("simulated unregister failure");
            return Task.CompletedTask;
        }
        public Task<DateTime?> GetNextFireTimeAsync(Guid triggerId, CancellationToken cancellationToken = default)
            => Task.FromResult<DateTime?>(null);
        public Task RegisterPollTriggerAsync(Guid triggerId, Guid workflowDefinitionId, int intervalSeconds, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task UnregisterPollTriggerAsync(Guid triggerId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
