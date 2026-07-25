using FlowEngine.Core;
using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Ai;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 定时触发器节点，按时间表触发工作流。
/// </summary>
public sealed class ScheduleTriggerNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "scheduleTrigger";

    /// <inheritdoc />
    AiNodeDefinition? INodeType.GetAiDefinition(NodeTypeDescriptor descriptor) =>
        AiDefinitionHelpers.Def(
            "Schedule Trigger", "Trigger", true,
            "按 Cron 表达式定时触发工作流，是工作流的入口节点。常用于每日/每小时周期性任务。",
            ["trigger", "schedule", "cron"],
            null,
            AiDefinitionHelpers.Example("每天 9 点触发",
                JsonNode.Parse("""{"cron":"0 9 * * *"}"""),
                JsonNode.Parse("""{"triggeredAt":"2026-07-12T09:00:00Z"}""")));

    /// <inheritdoc />
    public string DisplayName => "Schedule Trigger";

    /// <inheritdoc />
    public string Category => "Trigger";

    /// <inheritdoc />
    public string Icon => "clock";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 触发间隔。
    /// </summary>
    [Description("Trigger interval.")]
    public ScheduleInterval Interval { get; set; } = ScheduleInterval.Days;

    /// <summary>
    /// 间隔值。
    /// </summary>
    [Description("Number of intervals between triggers.")]
    public int IntervalValue { get; set; } = 1;

    /// <summary>
    /// Cron 表达式（可选，优先级高于 Interval）。
    /// </summary>
    [Description("Cron expression for complex schedules (e.g. '0 9 * * 1-5' for weekdays at 9am).")]
    public string? CronExpression { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => true;

    /// <inheritdoc />
    public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        // In a real implementation, this would be scheduled by a timer
        // For now, we just return the current time as trigger data

        var outputBatch = new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = new System.Text.Json.Nodes.JsonObject
                    {
                        ["timestamp"] = DateTime.UtcNow.ToString("o"),
                        ["interval"] = Interval.ToString(),
                        ["intervalValue"] = IntervalValue,
                        ["cronExpression"] = CronExpression
                    },
                    Success = true,
                    SourceIndex = 0
                }
            ]
        };

        return Task.FromResult(new NodeExecutionResult
        {
            Success = true,
            Output = outputBatch
        });
    }
}

/// <summary>
/// 调度间隔类型。
/// </summary>
public enum ScheduleInterval
{
    Seconds,
    Minutes,
    Hours,
    Days,
    Weeks,
    Months
}
