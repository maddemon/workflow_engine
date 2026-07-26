using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 等待节点，暂停工作流执行指定时长后透传输入批次。
/// 新写法继承 <see cref="NodeBase"/>，通过 [NodeMeta]/[Port] 声明式描述元信息与端口，
/// 业务只需计算延迟并等待，随后返回输入批次（统一由基类包装为执行结果）。
/// </summary>
[NodeMeta(TypeName = "wait", DisplayName = "Wait", Category = NodeCategory.Flow, Icon = "pause")]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output)]
public sealed class WaitNode : NodeBase
{
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
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        var waitTime = CalculateWaitTime();

        try
        {
            await Task.Delay(waitTime, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 取消视为节点级“已取消”业务结果，交由基类统一转换为 Cancelled 错误结果。
            throw new NodeExecutionException(FlowConstants.ErrorCodes.Cancelled, "等待被取消。");
        }

        return NodeHandlerOutput.Data(input.InputBatch);
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
