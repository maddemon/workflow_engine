using System.ComponentModel;

namespace FlowEngine.Core.Enums;

/// <summary>
/// 触发器类型。
/// </summary>
public enum TriggerType
{
    /// <summary>
    /// 定时触发器。
    /// </summary>
    [Description("定时触发器")]
    Schedule,

    /// <summary>
    /// Webhook 触发器。
    /// </summary>
    [Description("Webhook 触发器")]
    Webhook,

    /// <summary>
    /// 轮询触发器。
    /// </summary>
    [Description("轮询触发器")]
    Poll = 2,
}
