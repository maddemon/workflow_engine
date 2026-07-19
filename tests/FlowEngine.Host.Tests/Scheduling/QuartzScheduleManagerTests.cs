using System.Globalization;
using FlowEngine.Host.Jobs;
using FlowEngine.Host.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Quartz;

namespace FlowEngine.Host.Tests.Scheduling;

public sealed class QuartzScheduleManagerTests
{
    private readonly Mock<ISchedulerFactory> _schedulerFactoryMock = new();
    private readonly Mock<IScheduler> _schedulerMock = new();
    private readonly QuartzScheduleManager _manager;

    public QuartzScheduleManagerTests()
    {
        _schedulerFactoryMock
            .Setup(x => x.GetScheduler(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_schedulerMock.Object);
        _manager = new QuartzScheduleManager(_schedulerFactoryMock.Object, NullLogger<QuartzScheduleManager>.Instance);
    }

    [Fact]
    public async Task StartAsync_ReturnsCompletedTask()
    {
        await _manager.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(true);
    }

    [Fact]
    public async Task StopAsync_ReturnsCompletedTask()
    {
        await _manager.StopAsync(TestContext.Current.CancellationToken);
        Assert.True(true);
    }

    [Fact]
    public async Task RegisterScheduleAsync_NewJob_SchedulesJob()
    {
        var triggerId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();
        _schedulerMock.Setup(x => x.CheckExists(It.IsAny<JobKey>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _schedulerMock.Setup(x => x.ScheduleJob(It.IsAny<IJobDetail>(), It.IsAny<ITrigger>(), It.IsAny<CancellationToken>())).ReturnsAsync(DateTimeOffset.UtcNow);

        await _manager.RegisterScheduleAsync(triggerId, workflowDefinitionId, "0 0 * * * ?", cancellationToken: TestContext.Current.CancellationToken);

        _schedulerMock.Verify(x => x.CheckExists(It.Is<JobKey>(k => k.Name == $"schedule-trigger-{triggerId}"), It.IsAny<CancellationToken>()), Times.Once);
        _schedulerMock.Verify(x => x.ScheduleJob(It.IsAny<IJobDetail>(), It.IsAny<ITrigger>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterScheduleAsync_ExistingJob_DeletesBeforeSchedule()
    {
        var triggerId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();
        _schedulerMock.Setup(x => x.CheckExists(It.IsAny<JobKey>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _schedulerMock.Setup(x => x.DeleteJob(It.IsAny<JobKey>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _schedulerMock.Setup(x => x.ScheduleJob(It.IsAny<IJobDetail>(), It.IsAny<ITrigger>(), It.IsAny<CancellationToken>())).ReturnsAsync(DateTimeOffset.UtcNow);

        await _manager.RegisterScheduleAsync(triggerId, workflowDefinitionId, "0 0 * * * ?", cancellationToken: TestContext.Current.CancellationToken);

        _schedulerMock.Verify(x => x.DeleteJob(It.IsAny<JobKey>(), It.IsAny<CancellationToken>()), Times.Once);
        _schedulerMock.Verify(x => x.ScheduleJob(It.IsAny<IJobDetail>(), It.IsAny<ITrigger>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterScheduleAsync_WithTimeZoneAndStartEnd_SchedulesJob()
    {
        var triggerId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();
        var startAt = DateTime.UtcNow.AddHours(1);
        var endAt = DateTime.UtcNow.AddHours(2);
        _schedulerMock.Setup(x => x.CheckExists(It.IsAny<JobKey>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _schedulerMock.Setup(x => x.ScheduleJob(It.IsAny<IJobDetail>(), It.IsAny<ITrigger>(), It.IsAny<CancellationToken>())).ReturnsAsync(DateTimeOffset.UtcNow);

        await _manager.RegisterScheduleAsync(
            triggerId,
            workflowDefinitionId,
            "0 0 * * * ?",
            timeZone: "UTC",
            startAt: startAt,
            endAt: endAt,
            cancellationToken: TestContext.Current.CancellationToken);

        _schedulerMock.Verify(x => x.ScheduleJob(It.IsAny<IJobDetail>(), It.IsAny<ITrigger>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnregisterScheduleAsync_Existing_DeletesJob()
    {
        var triggerId = Guid.NewGuid();
        _schedulerMock.Setup(x => x.CheckExists(It.IsAny<JobKey>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _schedulerMock.Setup(x => x.DeleteJob(It.IsAny<JobKey>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await _manager.UnregisterScheduleAsync(triggerId, TestContext.Current.CancellationToken);

        _schedulerMock.Verify(x => x.DeleteJob(It.IsAny<JobKey>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnregisterScheduleAsync_NonExisting_DoesNotDelete()
    {
        var triggerId = Guid.NewGuid();
        _schedulerMock.Setup(x => x.CheckExists(It.IsAny<JobKey>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await _manager.UnregisterScheduleAsync(triggerId, TestContext.Current.CancellationToken);

        _schedulerMock.Verify(x => x.DeleteJob(It.IsAny<JobKey>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetNextFireTimeAsync_Existing_ReturnsUtcDateTime()
    {
        var triggerId = Guid.NewGuid();
        var nextFire = DateTimeOffset.UtcNow.AddMinutes(5);
        var triggerMock = new Mock<ITrigger>();
        triggerMock.Setup(x => x.GetNextFireTimeUtc()).Returns(nextFire);
        _schedulerMock.Setup(x => x.GetTrigger(It.IsAny<TriggerKey>(), It.IsAny<CancellationToken>())).ReturnsAsync(triggerMock.Object);

        var result = await _manager.GetNextFireTimeAsync(triggerId, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(nextFire.UtcDateTime, result.Value);
    }

    [Fact]
    public async Task GetNextFireTimeAsync_NonExisting_ReturnsNull()
    {
        var triggerId = Guid.NewGuid();
        _schedulerMock.Setup(x => x.GetTrigger(It.IsAny<TriggerKey>(), It.IsAny<CancellationToken>())).ReturnsAsync((ITrigger?)null);

        var result = await _manager.GetNextFireTimeAsync(triggerId, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task RegisterPollTriggerAsync_NewJob_SchedulesJob()
    {
        var triggerId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();
        _schedulerMock.Setup(x => x.CheckExists(It.IsAny<JobKey>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _schedulerMock.Setup(x => x.ScheduleJob(It.IsAny<IJobDetail>(), It.IsAny<ITrigger>(), It.IsAny<CancellationToken>())).ReturnsAsync(DateTimeOffset.UtcNow);

        await _manager.RegisterPollTriggerAsync(triggerId, workflowDefinitionId, 60, TestContext.Current.CancellationToken);

        _schedulerMock.Verify(x => x.CheckExists(It.Is<JobKey>(k => k.Name == $"poll-trigger-{triggerId}"), It.IsAny<CancellationToken>()), Times.Once);
        _schedulerMock.Verify(x => x.ScheduleJob(It.IsAny<IJobDetail>(), It.IsAny<ITrigger>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnregisterPollTriggerAsync_Existing_DeletesJob()
    {
        var triggerId = Guid.NewGuid();
        _schedulerMock.Setup(x => x.CheckExists(It.IsAny<JobKey>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _schedulerMock.Setup(x => x.DeleteJob(It.IsAny<JobKey>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await _manager.UnregisterPollTriggerAsync(triggerId, TestContext.Current.CancellationToken);

        _schedulerMock.Verify(x => x.DeleteJob(It.IsAny<JobKey>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
