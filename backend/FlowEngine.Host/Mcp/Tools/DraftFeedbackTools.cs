using System.ComponentModel;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Workflows;
using FlowEngine.Host.Mcp;
using ModelContextProtocol.Server;

namespace FlowEngine.Host.Mcp.Tools;

/// <summary>
/// 草稿反馈 MCP 工具——供外部 AI 拉取草稿审查反馈。
/// </summary>
[McpServerToolType]
public sealed class DraftFeedbackTools(IWorkflowService workflowService)
{
    /// <summary>
    /// 获取指定草稿的审查反馈（含拒绝理由/审查状态）。
    /// </summary>
    /// <param name="draftId">草稿工作流 ID（合法 Guid 格式）。</param>
    /// <returns>草稿反馈 DTO 或结构化错误。</returns>
    [McpServerTool(Name = "get_draft_feedback")]
    [Description("获取草稿工作流的人类审查反馈，包括拒绝理由和最近执行结果。外部 AI 可用此信息自纠并重新生成草稿。")]
    public async Task<object> GetDraftFeedback(
        [Description("草稿工作流 ID（合法 Guid 格式）。")] string draftId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draftId) || !Guid.TryParse(draftId, out var id))
        {
            return new McpToolError(
                "InvalidInput",
                "草稿 ID 格式无效",
                CanAutoFix: true,
                SuggestedFix: "请检查并修正输入参数");
        }

        var workflow = await workflowService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (workflow is null)
        {
            return new McpToolError(
                "NotFound",
                $"草稿 '{draftId}' 不存在",
                CanAutoFix: false,
                SuggestedFix: "请确认 ID 正确或先创建/装配");
        }

        // 返回草稿审查状态、拒绝理由等反馈信息
        return new
        {
            workflow.Id,
            workflow.Name,
            workflow.Source,
            workflow.DraftStatus,
            workflow.RejectionReason,
            workflow.IsActive,
            workflow.CreatedAt,
            workflow.UpdatedAt,
        };
    }
}
