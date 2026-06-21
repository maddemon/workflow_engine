using FlowEngine.Core.Abstractions;
using FlowEngine.Host.Jobs;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Spi;

namespace FlowEngine.Host.Scheduling;

/// <summary>
/// 基于 Quartz.NET 的调度管理器实现。
/// </summary>
public sealed class QuartzScheduleManager : IScheduleManager, IHostedService, IDisposable
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly ILogger<QuartzScheduleManager> _logger;
    private IScheduler? _scheduler;

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
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _scheduler = await _schedulerFactory.GetScheduler(cancellationToken).ConfigureAwait(false);
        await _scheduler.Start(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Quartz 调度器已启动。");
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_scheduler is not null)
        {
            await _scheduler.Shutdown(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Quartz 调度器已停止。");
        }
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
        if (_scheduler is null)
        {
            _logger.LogWarning("调度器未启动，无法注册触发器: {TriggerId}", triggerId);
            return;
        }

        var jobKey = new JobKey($"schedule-trigger-{triggerId}", "triggers");
        var triggerKey = new TriggerKey($"schedule-trigger-{triggerId}", "triggers");

        if (await _scheduler.CheckExists(jobKey, cancellationToken).ConfigureAwait(false))
        {
            await _scheduler.DeleteJob(jobKey, cancellationToken).ConfigureAwait(false);
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

        await _scheduler.ScheduleJob(job, quartzTrigger, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "已注册定时触发器: TriggerId={TriggerId}, Cron={Cron}",
            triggerId, cronExpression);
    }

    /// <inheritdoc />
    public async Task UnregisterScheduleAsync(Guid triggerId, CancellationToken cancellationToken = default)
    {
        if (_scheduler is null) return;

        var jobKey = new JobKey($"schedule-trigger-{triggerId}", "triggers");
        if (await _scheduler.CheckExists(jobKey, cancellationToken).ConfigureAwait(false))
        {
            await _scheduler.DeleteJob(jobKey, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("已注销定时触发器: TriggerId={TriggerId}", triggerId);
        }
    }

    /// <inheritdoc />
    public async Task<DateTime?> GetNextFireTimeAsync(Guid triggerId, CancellationToken cancellationToken = default)
    {
        if (_scheduler is null) return null;

        var triggerKey = new TriggerKey($"schedule-trigger-{triggerId}", "triggers");
        var trigger = await _scheduler.GetTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
        return trigger?.GetNextFireTimeUtc()?.UtcDateTime;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        (_scheduler as IDisposable)?.Dispose();
    }
}
