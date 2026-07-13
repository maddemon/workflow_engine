using System.ComponentModel;

namespace FlowEngine.Core.Enums;

/// <summary>
/// 工作流来源。
/// </summary>
public enum WorkflowSource
{
    /// <summary>
    /// 人类手动搭建或创建。
    /// </summary>
    [Description("人工创建")]
    Human = 0,

    /// <summary>
    /// AI 通过 assemble / modify 生成。
    /// </summary>
    [Description("AI 生成")]
    Ai = 1,
}
