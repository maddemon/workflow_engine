using System.ComponentModel;

namespace FlowEngine.Core.Enums;

/// <summary>
/// 节点错误处理策略。
/// </summary>
public enum ErrorStrategy
{
    /// <summary>
    /// 终止整个执行。
    /// </summary>
    [Description("终止执行")]
    Terminate,

    /// <summary>
    /// 注入错误数据项并继续执行下游。
    /// </summary>
    [Description("继续执行")]
    Continue,

    /// <summary>
    /// 按重试策略重试。
    /// </summary>
    [Description("重试")]
    Retry
}
