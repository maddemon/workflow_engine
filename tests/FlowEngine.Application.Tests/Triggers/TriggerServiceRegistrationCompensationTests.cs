using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
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

namespace FlowEngine.Application.Tests.Triggers;

/// <summary>
/// EX-3：触发器调度注册（外部 Quartz/Poll）在 DB 提交后失败时，必须补偿（回退为非激活）+ 告警，
/// 杜绝“已激活但调度未注册”的静默失效；注册成功则状态保持不变。
/// </summary>
public sealed class TriggerServiceRegistrationCompensationTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly RecordingEventBus _eventBus;
    private readonly FakeUserContext _userContext;

    public TriggerServiceRegistrationCompensationTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _eventBus = new RecordingEventBus();
        _userContext = new FakeUserContext { Roles = [RoleConstants.Admin] };
    }

    public void Dispose() => _dbContext.Dispose();

    private TriggerService BuildService(IScheduleManager scheduleManager)
    {
        var auditFactory = new AuditEventFactory(_userContext);
        var resourceAuthorization = new RoleBasedResourceAuthorizationService(_userContext);
        return new TriggerService(
            _dbContext,
            _eventBus,
            auditFactory,
            scheduleManager,
            AuthorizationGuardFactory.Create(_userContext, resourceAuthorization),
            new WebhookRouteService(_dbContext),
            NullLogger<TriggerService>.Instance);
    }

    private Guid SeedWorkflow()
    {
        var workflowId = Guid.NewGuid();
        _dbContext.Workflows.Add(new Workflow
        {
            Id = workflowId,
            Name = "wf",
            CreatedBy = "t",
            Nodes = [],
            Connections = [],
        });
        _dbContext.SaveChanges();
        return workflowId;
    }

    [Fact]
    public async Task CreateAsync_PollRegistrationFails_CompensatesToInactive()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = SeedWorkflow();
        var service = BuildService(new ThrowingScheduleManager());

        // 创建 Poll 触发器时注册失败：不应抛异常，触发器应回退为非激活。
        var dto = new CreateTriggerDto
        {
            WorkflowDefinitionId = workflowId,
            Type = TriggerType.Poll,
            Name = "poll",
            IsActive = true,
            Settings = new TriggerSettingsDto(),
        };

        var result = await service.CreateAsync(dto, ct);

        Assert.NotNull(result);
        var persisted = await _dbContext.Triggers.FirstOrDefaultAsync(t => t.Id == result.Id, ct);
        Assert.NotNull(persisted);
        Assert.False(persisted!.IsActive); // EX-3 补偿：回退为非激活，避免静默失效。
    }

    [Fact]
    public async Task CreateAsync_PollRegistrationSucceeds_StaysActive()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = SeedWorkflow();
        var service = BuildService(new FakeScheduleManager());

        var dto = new CreateTriggerDto
        {
            WorkflowDefinitionId = workflowId,
            Type = TriggerType.Poll,
            Name = "poll",
            IsActive = true,
            Settings = new TriggerSettingsDto(),
        };

        var result = await service.CreateAsync(dto, ct);

        var persisted = await _dbContext.Triggers.FirstOrDefaultAsync(t => t.Id == result.Id, ct);
        Assert.NotNull(persisted);
        Assert.True(persisted!.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_PollRegistrationFails_CompensatesToInactive()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflowId = SeedWorkflow();
        // 先用成功调度器创建（注册成功，IsActive=true）。
        var createService = BuildService(new FakeScheduleManager());
        var created = await createService.CreateAsync(new CreateTriggerDto
        {
            WorkflowDefinitionId = workflowId,
            Type = TriggerType.Poll,
            Name = "poll",
            IsActive = true,
            Settings = new TriggerSettingsDto(),
        }, ct);

        // 再用失败调度器更新（触发重新注册 → 失败 → 补偿）。
        var updateService = BuildService(new ThrowingScheduleManager());
        await updateService.UpdateAsync(created.Id, new UpdateTriggerDto
        {
            Name = "poll-updated",
            IsActive = true,
            Settings = new TriggerSettingsDto(),
        }, ct);

        var persisted = await _dbContext.Triggers.FirstOrDefaultAsync(t => t.Id == created.Id, ct);
        Assert.NotNull(persisted);
        Assert.False(persisted!.IsActive); // 补偿回退为非激活。
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
