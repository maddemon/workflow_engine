using System.ComponentModel;

namespace FlowEngine.Core.Enums;

/// <summary>
/// 工作流执行状态。
/// </summary>
public enum ExecutionStatus
{
    /// <summary>
    /// 待执行。
    /// </summary>
    [Description("待执行")]
    Pending,

    /// <summary>
    /// 执行中。
    /// </summary>
    [Description("执行中")]
    Running,

    /// <summary>
    /// 已完成。
    /// </summary>
    [Description("已完成")]
    Completed,

    /// <summary>
    /// 失败。
    /// </summary>
    [Description("失败")]
    Failed,

    /// <summary>
    /// 已取消。
    /// </summary>
    [Description("已取消")]
    Cancelled,

    /// <summary>
    /// 补偿中。
    /// </summary>
    [Description("补偿中")]
    Compensating,

    /// <summary>
    /// 已补偿。
    /// </summary>
    [Description("已补偿")]
    Compensated,

    /// <summary>
    /// 补偿失败。
    /// </summary>
    [Description("补偿失败")]
    CompensationFailed,

    /// <summary>
    /// 模拟运行完成。
    /// </summary>
    [Description("模拟运行完成")]
    DryRunCompleted
}
