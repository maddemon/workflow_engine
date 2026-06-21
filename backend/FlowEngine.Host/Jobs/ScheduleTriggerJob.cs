using FlowEngine.Application.Triggers;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Enums;
using FlowEngine.Core.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quartz;

namespace FlowEngine.Host.Jobs;

/// <summary>
/// Quartz 定时触发器 Job，按 Cron 表达式触发工作流执行。
/// </summary>
public sealed class ScheduleTriggerJob(
    IEngine engine,
    FlowEngineDbContext dbContext,
    TriggerService triggerService,
    ILogger<ScheduleTriggerJob> logger) : IJob
{
    /// <summary>
    /// JobDataMap 中触发器 ID 的键。
    /// </summary>
    public const string TriggerIdKey = "TriggerId";

    /// <summary>
    /// JobDataMap 中工作流定义 ID 的键。
    /// </summary>
    public const string WorkflowDefinitionIdKey = "WorkflowDefinitionId";

    /// <inheritdoc />
    public async Task Execute(IJobExecutionContext context)
    {
        var dataMap = context.MergedJobDataMap;
        var triggerId = dataMap.GetGuid(TriggerIdKey);
        var workflowDefinitionId = dataMap.GetGuid(WorkflowDefinitionIdKey);

        logger.LogInformation(
            "定时触发器执行: TriggerId={TriggerId}, WorkflowDefinitionId={WorkflowDefinitionId}",
            triggerId, workflowDefinitionId);

        var trigger = await dbContext.Triggers.FirstOrDefaultAsync(t => t.Id == triggerId, context.CancellationToken)
            .ConfigureAwait(false);

        if (trigger is null || !trigger.IsActive)
        {
            logger.LogWarning("定时触发器不存在或已停用: TriggerId={TriggerId}", triggerId);
            return;
        }

        try
        {
            var executionId = await engine.StartAsync(
                workflowDefinitionId,
                triggerPayload: new { triggerType = TriggerType.Schedule.ToString(), triggerId },
                context.CancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "定时触发器触发工作流成功: TriggerId={TriggerId}, ExecutionId={ExecutionId}",
                triggerId, executionId);

            var nextFireTime = context.Trigger.GetNextFireTimeUtc()?.UtcDateTime;
            await triggerService.UpdateTriggerTimestampsAsync(
                triggerId,
                DateTime.UtcNow,
                nextFireTime,
                context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "定时触发器触发工作流失败: TriggerId={TriggerId}",
                triggerId);
        }
    }
}
