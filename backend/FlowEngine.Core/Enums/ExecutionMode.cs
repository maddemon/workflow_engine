using System.ComponentModel;

namespace FlowEngine.Core.Enums;

/// <summary>
/// 节点执行模式。
/// </summary>
public enum ExecutionMode
{
    /// <summary>
    /// 对整个数据批次执行一次。
    /// </summary>
    [Description("对整个批次执行一次")]
    OnceForAll,

    /// <summary>
    /// 对数据批次中每条数据项分别执行一次。
    /// </summary>
    [Description("对每条数据项分别执行")]
    OncePerItem
}
