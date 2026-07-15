using FlowEngine.Core;
using System.ComponentModel;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 等待节点，暂停工作流执行。
/// </summary>
public sealed class WaitNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "wait";

    /// <inheritdoc />
    public string DisplayName => "Wait";

    /// <inheritdoc />
    public string Category => "Core";

    /// <inheritdoc />
    public string Icon => "pause";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 等待时间。
    /// </summary>
    [Description("Amount of time to wait.")]
    public int Amount { get; set; } = 1;

    /// <summary>
    /// 时间单位。
    /// </summary>
    [Description("Time unit for the wait amount.")]
    public WaitUnit Unit { get; set; } = WaitUnit.Seconds;

    /// <summary>
    /// 是否限制等待时间。
    /// </summary>
    [Description("Whether to limit the wait time.")]
    public bool LimitWaitTime { get; set; } = false;

    /// <summary>
    /// 最大等待时间。
    /// </summary>
    [Description("Maximum time to wait before resuming.")]
    public int MaxWaitAmount { get; set; } = 60;

    /// <summary>
    /// 最大等待时间单位。
    /// </summary>
    [Description("Time unit for the maximum wait amount.")]
    public WaitUnit MaxWaitUnit { get; set; } = WaitUnit.Seconds;

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        var inputBatch = context.GetInputBatch();
        var waitTime = CalculateWaitTime();

        return await context.CatchToResult(async ct =>
        {
            await Task.Delay(waitTime, ct).ConfigureAwait(false);
            return context.Ok(inputBatch);
        }, cancellationToken).ConfigureAwait(false);
    }

    private TimeSpan CalculateWaitTime()
    {
        var effectiveAmount = Math.Max(0, Amount);
        var totalSeconds = Unit switch
        {
            WaitUnit.Seconds => effectiveAmount,
            WaitUnit.Minutes => effectiveAmount * 60,
            WaitUnit.Hours => effectiveAmount * 3600,
            WaitUnit.Days => effectiveAmount * 86400,
            _ => effectiveAmount
        };

        var maxSeconds = MaxWaitUnit switch
        {
            WaitUnit.Seconds => MaxWaitAmount,
            WaitUnit.Minutes => MaxWaitAmount * 60,
            WaitUnit.Hours => MaxWaitAmount * 3600,
            WaitUnit.Days => MaxWaitAmount * 86400,
            _ => MaxWaitAmount
        };

        var effectiveSeconds = LimitWaitTime ? Math.Min(totalSeconds, maxSeconds) : totalSeconds;
        return TimeSpan.FromSeconds(effectiveSeconds);
    }
}

/// <summary>
/// 等待时间单位。
/// </summary>
public enum WaitUnit
{
    Seconds,
    Minutes,
    Hours,
    Days
}
