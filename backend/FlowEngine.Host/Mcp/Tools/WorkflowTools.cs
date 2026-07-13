using System.ComponentModel;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Exceptions;
using ModelContextProtocol.Server;

namespace FlowEngine.Host.Mcp.Tools;

/// <summary>
/// Workflow 草稿 MCP 工具，供 AI 装配与修改工作流。
/// </summary>
[McpServerToolType]
public sealed class WorkflowTools(
    IWorkflowAssemblyService assemblyService,
    IWorkflowModificationService modificationService)
{
    /// <summary>
    /// 根据 AI 提供的极简草稿装配完整工作流。
    /// AI 只需填写节点 ID、类型名和参数，以及节点间的连接；后端会自动补全端口、坐标、入口节点，并创建未激活的草稿。
    /// </summary>
    /// <param name="name">工作流名称。</param>
    /// <param name="projectId">项目 ID（可选）。</param>
    /// <param name="nodes">节点列表，每项含 id（可选）、typeName（必需）、parameters（可选）。</param>
    /// <param name="connections">连接列表，每项含 from（必需）、to（必需）、fromPort（可选）、toPort（可选）。</param>
    /// <returns>装配结果 { draftId, workflow } 或结构化错误。</returns>
    [McpServerTool(Name = "assemble_workflow")]
    [Description("根据 AI 提供的极简草稿装配完整工作流。AI 只需填写节点 ID、类型名和参数，以及节点间的连接；后端会自动补全端口、坐标、入口节点，并创建未激活的草稿。")]
    public async Task<object> AssembleWorkflow(
        [Description("工作流名称。")] string name,
        [Description("节点列表，每项含 id（可选）、typeName（必需）、parameters（可选）。")] List<AiDraftNodeDto> nodes,
        [Description("项目 ID（可选）。")] string? projectId = null,
        [Description("连接列表，每项含 from（必需）、to（必需）、fromPort（可选）、toPort（可选）。")] List<AiDraftConnectionDto>? connections = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new { success = false, errorCode = "InvalidInput", message = "工作流名称不能为空" };
        }

        if (nodes is null || nodes.Count == 0)
        {
            return new { success = false, errorCode = "InvalidInput", message = "节点列表不能为空" };
        }

        Guid? projectIdValue = null;
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            if (!Guid.TryParse(projectId, out var pid))
                return new { success = false, errorCode = "InvalidInput", message = "项目 ID 格式无效" };
            projectIdValue = pid;
        }

        var request = new AssembleWorkflowRequest
        {
            Name = name,
            ProjectId = projectIdValue,
            Nodes = nodes,
            Connections = connections ?? [],
        };

        try
        {
            var result = await assemblyService.AssembleAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return result;
        }
        catch (BusinessException ex)
        {
            return new { success = false, errorCode = "AssembleFailed", message = ex.Message };
        }
    }

    /// <summary>
    /// 基于已有工作流创建新草稿，并应用 AI 指定的结构化修改操作（add/remove/modify/connect/disconnect）。
    /// 返回新草稿 ID、完整工作流和差异列表。
    /// </summary>
    /// <param name="workflowId">源工作流 ID。</param>
    /// <param name="operations">修改操作列表，每项含 op（必需）及其他按操作类型所需字段。</param>
    /// <returns>修改结果 { draftId, workflow, diff } 或结构化错误。</returns>
    [McpServerTool(Name = "modify_workflow")]
    [Description("基于已有工作流创建新草稿，并应用 AI 指定的结构化修改操作（add/remove/modify/connect/disconnect）。返回新草稿 ID、完整工作流和差异列表。")]
    public async Task<object> ModifyWorkflow(
        [Description("源工作流 ID。")] string workflowId,
        [Description("修改操作列表，每项含 op（必需）及其他按操作类型所需字段。")] List<WorkflowOperation> operations,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workflowId) || !Guid.TryParse(workflowId, out var wid) || wid == Guid.Empty)
        {
            return new { success = false, errorCode = "InvalidInput", message = "工作流 ID 无效" };
        }

        if (operations is null || operations.Count == 0)
        {
            return new { success = false, errorCode = "InvalidInput", message = "操作列表不能为空" };
        }

        var request = new ModifyWorkflowRequest
        {
            Operations = operations,
        };

        try
        {
            var result = await modificationService.ModifyAsync(wid, request, cancellationToken)
                .ConfigureAwait(false);
            return result;
        }
        catch (BusinessException ex)
        {
            return new { success = false, errorCode = "ModifyFailed", message = ex.Message };
        }
    }
}
