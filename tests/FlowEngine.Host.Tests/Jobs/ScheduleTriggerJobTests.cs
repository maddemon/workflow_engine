using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Triggers;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Host.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Quartz;

namespace FlowEngine.Host.Tests.Jobs;

public sealed class ScheduleTriggerJobTests
{
    private static FlowEngineDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FlowEngineDbContext(options);
    }

    private static TriggerService CreateTriggerService(FlowEngineDbContext dbContext)
    {
        var eventBusMock = new Mock<IEventBus>();
        var scheduleManagerMock = new Mock<IScheduleManager>();
        var authGuardMock = new Mock<IAuthorizationGuard>();
        var userContextMock = new Mock<IUserContext>();
        var webhookRouteService = new WebhookRouteService(dbContext);
        var auditFactory = new AuditEventFactory(userContextMock.Object);
        return new TriggerService(
            dbContext,
            eventBusMock.Object,
            auditFactory,
            scheduleManagerMock.Object,
            authGuardMock.Object,
            webhookRouteService,
            NullLogger<TriggerService>.Instance);
    }

    [Fact]
    public async Task Execute_ActiveTrigger_StartsWorkflowAndUpdatesTimestamps()
    {
        var triggerId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();
        var executionId = ExecutionId.From(Guid.NewGuid());
        await using var dbContext = CreateDbContext();
        dbContext.Triggers.Add(new Trigger
        {
            Id = triggerId,
            WorkflowDefinitionId = workflowDefinitionId,
            Type = TriggerType.Schedule,
            Name = "Test",
            IsActive = true,
            Settings = new TriggerSettings(),
        });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var engineMock = new Mock<IEngine>();
        engineMock.Setup(x => x.StartAsync(
                workflowDefinitionId,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(executionId);
        var triggerService = CreateTriggerService(dbContext);
        var job = new ScheduleTriggerJob(
            engineMock.Object,
            dbContext,
            triggerService,
            NullLogger<ScheduleTriggerJob>.Instance);

        var contextMock = CreateContext(triggerId, workflowDefinitionId);

        await job.Execute(contextMock.Object);

        engineMock.Verify(x => x.StartAsync(
            workflowDefinitionId,
            It.IsAny<object?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        var updated = await dbContext.Triggers.FirstAsync(t => t.Id == triggerId, TestContext.Current.CancellationToken);
        Assert.NotNull(updated.LastTriggeredAt);
    }

    [Fact]
    public async Task Execute_InactiveTrigger_DoesNotStartWorkflow()
    {
        var triggerId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        dbContext.Triggers.Add(new Trigger
        {
            Id = triggerId,
            WorkflowDefinitionId = workflowDefinitionId,
            Type = TriggerType.Schedule,
            Name = "Test",
            IsActive = false,
            Settings = new TriggerSettings(),
        });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var engineMock = new Mock<IEngine>();
        var triggerService = CreateTriggerService(dbContext);
        var job = new ScheduleTriggerJob(
            engineMock.Object,
            dbContext,
            triggerService,
            NullLogger<ScheduleTriggerJob>.Instance);

        var contextMock = CreateContext(triggerId, workflowDefinitionId);

        await job.Execute(contextMock.Object);

        engineMock.Verify(x => x.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_MissingTrigger_DoesNotStartWorkflow()
    {
        var triggerId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();

        var engineMock = new Mock<IEngine>();
        var triggerService = CreateTriggerService(dbContext);
        var job = new ScheduleTriggerJob(
            engineMock.Object,
            dbContext,
            triggerService,
            NullLogger<ScheduleTriggerJob>.Instance);

        var contextMock = CreateContext(triggerId, workflowDefinitionId);

        await job.Execute(contextMock.Object);

        engineMock.Verify(x => x.StartAsync(It.IsAny<Guid>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_EngineThrows_LogsErrorAndDoesNotUpdateTimestamps()
    {
        var triggerId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        dbContext.Triggers.Add(new Trigger
        {
            Id = triggerId,
            WorkflowDefinitionId = workflowDefinitionId,
            Type = TriggerType.Schedule,
            Name = "Test",
            IsActive = true,
            Settings = new TriggerSettings(),
        });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var engineMock = new Mock<IEngine>();
        engineMock.Setup(x => x.StartAsync(
                workflowDefinitionId,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var triggerService = CreateTriggerService(dbContext);
        var job = new ScheduleTriggerJob(
            engineMock.Object,
            dbContext,
            triggerService,
            NullLogger<ScheduleTriggerJob>.Instance);

        var contextMock = CreateContext(triggerId, workflowDefinitionId);

        await job.Execute(contextMock.Object);

        var updated = await dbContext.Triggers.FirstAsync(t => t.Id == triggerId, TestContext.Current.CancellationToken);
        Assert.Null(updated.LastTriggeredAt);
    }

    private static Mock<IJobExecutionContext> CreateContext(Guid triggerId, Guid workflowDefinitionId)
    {
        var dataMap = new JobDataMap
        {
            { ScheduleTriggerJob.TriggerIdKey, triggerId },
            { ScheduleTriggerJob.WorkflowDefinitionIdKey, workflowDefinitionId },
        };
        var contextMock = new Mock<IJobExecutionContext>();
        contextMock.Setup(x => x.MergedJobDataMap).Returns(dataMap);
        contextMock.Setup(x => x.CancellationToken).Returns(TestContext.Current.CancellationToken);
        var triggerMock = new Mock<ITrigger>();
        contextMock.Setup(x => x.Trigger).Returns(triggerMock.Object);
        return contextMock;
    }
}
