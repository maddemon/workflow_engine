using System.ComponentModel.DataAnnotations.Schema;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 工作流样式设置。
/// </summary>
[NotMapped]
public sealed class WorkflowStyleSettings
{
    /// <summary>
    /// 布局方向：vertical（上下）或 horizontal（左右）。
    /// </summary>
    public string LayoutDirection { get; set; } = "vertical";
}
