using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Projects;
using FlowEngine.Application.Tests.TestSupport.Fakes;
using FlowEngine.Application.Triggers;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Application.Tests.Projects;

/// <summary>
/// GAP 1（D-1 / EX-3 一致性）：项目级联软删须先注销该项目触发器的外部 Quartz 调度，
/// 否则工作流被软删后 ExecutionService 加载为 null 而调度残留、静默 no-op。
/// </summary>
public sealed class ProjectCascadeScheduleTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly RecordingEventBus _eventBus;
    private readonly FakeUserContext _userContext;
    private readonly RecordingScheduleManager _scheduleManager;
    private readonly ProjectService _service;

    public ProjectCascadeScheduleTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _eventBus = new RecordingEventBus();
        _userContext = new FakeUserContext { Roles = [RoleConstants.Admin] };
        var auditFactory = new AuditEventFactory(_userContext);
        var authGuard = AuthorizationGuardFactory.Create(_userContext, new RoleBasedResourceAuthorizationService(_userContext), _eventBus);
        var handler = new AuthorizedOperationHandler(authGuard, _eventBus, auditFactory);
        _scheduleManager = new RecordingScheduleManager();
        var triggerService = new TriggerService(
            _dbContext, _eventBus, auditFactory, _scheduleManager, authGuard, new WebhookRouteService(_dbContext), NullLogger<TriggerService>.Instance);
        var cascadeDeleter = new ProjectCascadeDeleter(_dbContext, triggerService, NullLogger<ProjectCascadeDeleter>.Instance);
        _service = new ProjectService(_dbContext, _userContext, authGuard, _eventBus, auditFactory, handler, cascadeDeleter);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task DeleteAsync_Cascade_UnregistersProjectTriggerSchedules()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = new Project { Name = "P", CreatedBy = _userContext.UserId!.Value };
        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync(ct);

        var workflow = new Workflow
        {
            ProjectId = project.Id,
            Name = "wf",
            IsActive = true,
            Nodes = [],
            Connections = [],
            CreatedBy = "tester",
        };
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(ct);

        var scheduleTrigger = new Trigger
        {
            WorkflowDefinitionId = workflow.Id,
            ProjectId = project.Id,
            Type = TriggerType.Schedule,
            Name = "sched",
            IsActive = true,
            Settings = new TriggerSettings { CronExpression = "0 0 * * *" },
        };
        var pollTrigger = new Trigger
        {
            WorkflowDefinitionId = workflow.Id,
            ProjectId = project.Id,
            Type = TriggerType.Poll,
            Name = "poll",
            IsActive = true,
            Settings = new TriggerSettings { IntervalSeconds = 30 },
        };
        _dbContext.Triggers.AddRange(scheduleTrigger, pollTrigger);
        await _dbContext.SaveChangesAsync(ct);

        await _service.DeleteAsync(project.Id, ct);

        // 调度/轮询触发器均已注销（GAP 1 修复点）。
        Assert.Contains(scheduleTrigger.Id, _scheduleManager.UnregisteredSchedules);
        Assert.Contains(pollTrigger.Id, _scheduleManager.UnregisteredPolls);

        // 级联软删生效（D-1）。
        var schedReload = await _dbContext.Triggers.IgnoreQueryFilters().FirstAsync(t => t.Id == scheduleTrigger.Id, ct);
        Assert.True(schedReload.Deleted);
        var wfReload = await _dbContext.Workflows.IgnoreQueryFilters().FirstAsync(w => w.Id == workflow.Id, ct);
        Assert.True(wfReload.Deleted);
    }

    [Fact]
    public async Task DeleteAsync_Cascade_UnregisterFailure_DoesNotBlockSoftDelete()
    {
        var ct = TestContext.Current.CancellationToken;
        _scheduleManager.ThrowOnUnregister = true;

        var project = new Project { Name = "P", CreatedBy = _userContext.UserId!.Value };
        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync(ct);

        var workflow = new Workflow
        {
            ProjectId = project.Id,
            Name = "wf",
            IsActive = true,
            Nodes = [],
            Connections = [],
            CreatedBy = "tester",
        };
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(ct);

        var scheduleTrigger = new Trigger
        {
            WorkflowDefinitionId = workflow.Id,
            ProjectId = project.Id,
            Type = TriggerType.Schedule,
            Name = "sched",
            IsActive = true,
            Settings = new TriggerSettings { CronExpression = "0 0 * * *" },
        };
        _dbContext.Triggers.Add(scheduleTrigger);
        await _dbContext.SaveChangesAsync(ct);

        // 注销失败应被兜底，不阻断项目级联软删。
        await _service.DeleteAsync(project.Id, ct);

        var wfReload = await _dbContext.Workflows.IgnoreQueryFilters().FirstAsync(w => w.Id == workflow.Id, ct);
        Assert.True(wfReload.Deleted);
    }

    private sealed class RecordingScheduleManager : IScheduleManager
    {
        public List<Guid> UnregisteredSchedules { get; } = [];
        public List<Guid> UnregisteredPolls { get; } = [];
        public bool ThrowOnUnregister { get; set; }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RegisterScheduleAsync(Guid triggerId, Guid workflowDefinitionId, string cronExpression, string? timeZone = null, DateTime? startAt = null, DateTime? endAt = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task UnregisterScheduleAsync(Guid triggerId, CancellationToken cancellationToken = default)
        {
            if (ThrowOnUnregister) throw new InvalidOperationException("simulated unregister failure");
            UnregisteredSchedules.Add(triggerId);
            return Task.CompletedTask;
        }
        public Task<DateTime?> GetNextFireTimeAsync(Guid triggerId, CancellationToken cancellationToken = default)
            => Task.FromResult<DateTime?>(null);
        public Task RegisterPollTriggerAsync(Guid triggerId, Guid workflowDefinitionId, int intervalSeconds, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task UnregisterPollTriggerAsync(Guid triggerId, CancellationToken cancellationToken = default)
        {
            if (ThrowOnUnregister) throw new InvalidOperationException("simulated unregister failure");
            UnregisteredPolls.Add(triggerId);
            return Task.CompletedTask;
        }
    }
}
