using FlowEngine.Core.Abstractions;
using FlowEngine.Host.Jobs;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Spi;

namespace FlowEngine.Host.Scheduling;

/// <summary>
/// 基于 Quartz.NET 的调度管理器实现。
/// 调度器生命周期由 <c>AddQuartzHostedService</c> 管理的 <c>QuartzHostedService</c> 负责，
/// 本类通过 <see cref="ISchedulerFactory.GetScheduler"/> 幂等获取已启动的调度器实例。
/// </summary>
public sealed class QuartzScheduleManager : IScheduleManager
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly ILogger<QuartzScheduleManager> _logger;

    /// <summary>
    /// 初始化 Quartz 调度管理器。
    /// </summary>
    public QuartzScheduleManager(
        ISchedulerFactory schedulerFactory,
        ILogger<QuartzScheduleManager> logger)
    {
        _schedulerFactory = schedulerFactory ?? throw new ArgumentNullException(nameof(schedulerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        // 调度器生命周期由 AddQuartzHostedService 管理的 QuartzHostedService 负责，
        // 此处无需重复启动。
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        // 调度器关闭由 QuartzHostedService 负责。
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RegisterScheduleAsync(
        Guid triggerId,
        Guid workflowDefinitionId,
        string cronExpression,
        string? timeZone = null,
        DateTime? startAt = null,
        DateTime? endAt = null,
        CancellationToken cancellationToken = default)
    {
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken).ConfigureAwait(false);

        var jobKey = new JobKey($"schedule-trigger-{triggerId}", "triggers");
        var triggerKey = new TriggerKey($"schedule-trigger-{triggerId}", "triggers");

        if (await scheduler.CheckExists(jobKey, cancellationToken).ConfigureAwait(false))
        {
            await scheduler.DeleteJob(jobKey, cancellationToken).ConfigureAwait(false);
        }

        var job = JobBuilder.Create<ScheduleTriggerJob>()
            .WithIdentity(jobKey)
            .UsingJobData(ScheduleTriggerJob.TriggerIdKey, triggerId.ToString())
            .UsingJobData(ScheduleTriggerJob.WorkflowDefinitionIdKey, workflowDefinitionId.ToString())
            .Build();

        var tz = timeZone is not null ? TimeZoneInfo.FindSystemTimeZoneById(timeZone) : TimeZoneInfo.Utc;

        var scheduleBuilder = CronScheduleBuilder.CronSchedule(cronExpression)
            .InTimeZone(tz);

        var quartzTriggerBuilder = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .WithSchedule(scheduleBuilder);

        if (startAt.HasValue)
        {
            quartzTriggerBuilder.StartAt(startAt.Value);
        }

        if (endAt.HasValue)
        {
            quartzTriggerBuilder.EndAt(endAt.Value);
        }

        var quartzTrigger = quartzTriggerBuilder.Build();

        await scheduler.ScheduleJob(job, quartzTrigger, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "已注册定时触发器: TriggerId={TriggerId}, Cron={Cron}",
            triggerId, cronExpression);
    }

    /// <inheritdoc />
    public async Task UnregisterScheduleAsync(Guid triggerId, CancellationToken cancellationToken = default)
    {
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken).ConfigureAwait(false);

        var jobKey = new JobKey($"schedule-trigger-{triggerId}", "triggers");
        if (await scheduler.CheckExists(jobKey, cancellationToken).ConfigureAwait(false))
        {
            await scheduler.DeleteJob(jobKey, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("已注销定时触发器: TriggerId={TriggerId}", triggerId);
        }
    }

    /// <inheritdoc />
    public async Task<DateTime?> GetNextFireTimeAsync(Guid triggerId, CancellationToken cancellationToken = default)
    {
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken).ConfigureAwait(false);

        var triggerKey = new TriggerKey($"schedule-trigger-{triggerId}", "triggers");
        var trigger = await scheduler.GetTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
        return trigger?.GetNextFireTimeUtc()?.UtcDateTime;
    }

    /// <inheritdoc />
    public async Task RegisterPollTriggerAsync(
        Guid triggerId,
        Guid workflowDefinitionId,
        int intervalSeconds,
        CancellationToken cancellationToken = default)
    {
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken).ConfigureAwait(false);

        var jobKey = new JobKey($"poll-trigger-{triggerId}", "triggers");
        var triggerKey = new TriggerKey($"poll-trigger-{triggerId}", "triggers");

        if (await scheduler.CheckExists(jobKey, cancellationToken).ConfigureAwait(false))
        {
            await scheduler.DeleteJob(jobKey, cancellationToken).ConfigureAwait(false);
        }

        var job = JobBuilder.Create<PollTriggerJob>()
            .WithIdentity(jobKey)
            .UsingJobData(PollTriggerJob.TriggerIdKey, triggerId.ToString())
            .UsingJobData(PollTriggerJob.WorkflowDefinitionIdKey, workflowDefinitionId.ToString())
            .Build();

        var scheduleBuilder = SimpleScheduleBuilder.Create()
            .WithIntervalInSeconds(intervalSeconds)
            .RepeatForever();

        var quartzTrigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .WithSchedule(scheduleBuilder)
            .Build();

        await scheduler.ScheduleJob(job, quartzTrigger, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "已注册轮询触发器: TriggerId={TriggerId}, Interval={IntervalSeconds}s",
            triggerId, intervalSeconds);
    }

    /// <inheritdoc />
    public async Task UnregisterPollTriggerAsync(Guid triggerId, CancellationToken cancellationToken = default)
    {
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken).ConfigureAwait(false);

        var jobKey = new JobKey($"poll-trigger-{triggerId}", "triggers");
        if (await scheduler.CheckExists(jobKey, cancellationToken).ConfigureAwait(false))
        {
            await scheduler.DeleteJob(jobKey, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("已注销轮询触发器: TriggerId={TriggerId}", triggerId);
        }
    }
}
