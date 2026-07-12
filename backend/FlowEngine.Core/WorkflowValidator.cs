using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;

namespace FlowEngine.Core;

/// <summary>
/// 工作流定义校验工具。
/// </summary>
public static class WorkflowValidator
{
    /// <summary>
    /// 确保工作流不为空且包含节点。
    /// </summary>
    /// <param name="workflow">工作流定义。</param>
    /// <param name="errorCode">当校验失败时的错误码。</param>
    /// <returns>校验通过返回 null；否则返回错误信息。</returns>
    public static string? EnsureNonEmpty(Workflow? workflow, string errorCode = "EmptyWorkflow")
    {
        if (workflow is null)
        {
            return "Workflow definition is null.";
        }
        
        if (workflow.Nodes is null || workflow.Nodes.Count == 0)
        {
            return "The workflow contains no nodes.";
        }
        
        return null;
    }
}
