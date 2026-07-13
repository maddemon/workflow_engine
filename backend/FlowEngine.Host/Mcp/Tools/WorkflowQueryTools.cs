using System.ComponentModel;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Workflows;
using ModelContextProtocol.Server;

namespace FlowEngine.Host.Mcp.Tools;

/// <summary>
/// Workflow 查询 MCP 工具，供 AI 查看与列出工作流。
/// </summary>
[McpServerToolType]
public sealed class WorkflowQueryTools(IWorkflowService workflowService)
{
    /// <summary>
    /// 获取指定工作流的完整定义（包含所有节点、连接和参数）。用于 AI 查看已有工作流的结构。
    /// </summary>
    /// <param name="workflowId">工作流 ID（合法 Guid 格式）。</param>
    /// <returns>工作流 DTO 或结构化错误。</returns>
    [McpServerTool(Name = "get_workflow")]
    [Description("获取指定工作流的完整定义（包含所有节点、连接和参数）。用于 AI 查看已有工作流的结构。")]
    public async Task<object> GetWorkflow(
        [Description("工作流 ID（合法 Guid 格式）。")] string workflowId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workflowId) || !Guid.TryParse(workflowId, out var id))
        {
            return new McpToolError(
                "InvalidInput",
                "工作流 ID 格式无效",
                CanAutoFix: true,
                SuggestedFix: "请检查并修正输入参数");
        }

        var workflow = await workflowService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (workflow is null)
        {
            return new McpToolError(
                "NotFound",
                $"工作流 '{workflowId}' 不存在",
                CanAutoFix: false,
                SuggestedFix: "请确认 ID 正确或先创建/装配");
        }

        return workflow;
    }

    /// <summary>
    /// 列出工作流。可按项目 ID 过滤，支持分页。用于 AI 查找现有工作流。
    /// </summary>
    /// <param name="projectId">项目 ID（可选，合法 Guid 格式）。</param>
    /// <param name="page">页码（默认 1）。</param>
    /// <param name="pageSize">每页大小（默认 20，范围 1–200）。</param>
    /// <returns>分页工作流列表。</returns>
    [McpServerTool(Name = "list_workflows")]
    [Description("列出工作流。可按项目 ID 过滤，支持分页。用于 AI 查找现有工作流。")]
    public async Task<object> ListWorkflows(
        [Description("项目 ID（可选，合法 Guid 格式）。")] string? projectId = null,
        [Description("页码（默认 1）。")] int page = 1,
        [Description("每页大小（默认 20，范围 1–200）。")] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (pageSize < 1 || pageSize > 200)
        {
            return new McpToolError(
                "InvalidInput",
                "pageSize 必须在 1 到 200 之间",
                CanAutoFix: true,
                SuggestedFix: "请检查并修正输入参数");
        }

        Guid? projectIdValue = null;
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            if (!Guid.TryParse(projectId, out var pid))
            {
                return new McpToolError(
                    "InvalidInput",
                    "项目 ID 格式无效",
                    CanAutoFix: true,
                    SuggestedFix: "请检查并修正输入参数");
            }

            projectIdValue = pid;
        }

        var result = await workflowService.GetAllAsync(projectIdValue, page, pageSize, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }
}
