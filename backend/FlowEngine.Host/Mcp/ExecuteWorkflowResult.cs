using FlowEngine.Application.Dtos;

namespace FlowEngine.Host.Mcp;

/// <summary>
/// <see cref="WorkflowLifecycleTools.ExecuteWorkflow"/> 的执行结果包装。
/// 当执行产生失败节点记录时，除返回执行 DTO 外，附带结构化反馈（含执行上下文、建议修复、可自纠标记），供 AI 自纠。
/// </summary>
/// <param name="Execution">执行 DTO，含执行 ID 与状态。</param>
/// <param name="Feedback">执行反馈结果，含失败节点的执行上下文与建议修复方案。</param>
public sealed record ExecuteWorkflowResult(ExecutionDto Execution, ExecutionFeedbackResult Feedback)
{
    /// <summary>
    /// 是否成功。取反馈结果的成功标记，便于 AI 统一判断是否需要自纠。
    /// </summary>
    public bool Success => Feedback.Success;
}
