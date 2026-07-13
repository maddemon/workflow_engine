using System.ComponentModel;

namespace FlowEngine.Core.Enums;

/// <summary>
/// 草稿审查状态。
/// </summary>
public enum DraftStatus
{
    /// <summary>
    /// 待审查——AI 已生成草稿，人类尚未处理。
    /// </summary>
    [Description("待审查")]
    Pending = 0,

    /// <summary>
    /// 已拒绝——人类拒绝此草稿，含拒绝理由。
    /// </summary>
    [Description("已拒绝")]
    Rejected = 1,

    /// <summary>
    /// 已确认——人类确认并激活此草稿。
    /// </summary>
    [Description("已确认")]
    Confirmed = 2,
}
