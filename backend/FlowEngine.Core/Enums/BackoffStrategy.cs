using System.ComponentModel;

namespace FlowEngine.Core.Enums;

/// <summary>
/// 退避策略。
/// </summary>
public enum BackoffStrategy
{
    /// <summary>
    /// 指数退避：delay = baseDelay * 2^attempt。
    /// </summary>
    [Description("指数退避")]
    Exponential,

    /// <summary>
    /// 线性退避：delay = baseDelay * (attempt + 1)。
    /// </summary>
    [Description("线性退避")]
    Linear,

    /// <summary>
    /// 固定间隔：delay = baseDelay。
    /// </summary>
    [Description("固定间隔")]
    Fixed
}
