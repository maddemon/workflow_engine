using System.ComponentModel;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Executions;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Exceptions;
using ModelContextProtocol.Server;

namespace FlowEngine.Host.Mcp.Tools;

/// <summary>
/// Workflow 生命周期 MCP 工具，供 AI 校验、确认与执行工作流。
/// </summary>
[McpServerToolType]
public sealed class WorkflowLifecycleTools(
    IWorkflowValidationService validationService,
    IWorkflowService workflowService,
    IExecutionService executionService)
{
    /// <summary>
    /// 校验工作流定义的结构完整性。返回详细的错误列表，包含节点 ID、字段、错误类型和建议修复方案，供 AI 自纠。不抛协议异常。
    /// </summary>
    /// <param name="workflowId">已存在的工作流 ID（与 nodes/connections 二选一）。</param>
    /// <param name="nodes">草稿节点列表（与 workflowId 二选一）。</param>
    /// <param name="connections">草稿连接列表（与 workflowId 二选一）。</param>
    /// <returns>校验结果，包含 valid、errors[]、canAutoFix、retryCount、maxRetries。</returns>
    [McpServerTool(Name = "validate_workflow")]
    [Description("校验工作流定义的结构完整性。返回详细的错误列表，包含节点 ID、字段、错误类型和建议修复方案，供 AI 自纠。不抛协议异常。")]
    public async Task<ValidateWorkflowResult> ValidateWorkflow(
        [Description("已存在的工作流 ID（与 nodes/connections 二选一）。")] string? workflowId = null,
        [Description("草稿节点列表（与 workflowId 二选一）。")] List<NodeDefinitionDto>? nodes = null,
        [Description("草稿连接列表（与 workflowId 二选一）。")] List<ConnectionDto>? connections = null,
        CancellationToken cancellationToken = default)
    {
        Guid? workflowIdValue = null;
        if (!string.IsNullOrWhiteSpace(workflowId))
        {
            if (!Guid.TryParse(workflowId, out var wid))
            {
                return new ValidateWorkflowResult
                {
                    Valid = false,
                    Errors =
                    [
                        new ValidationError
                        {
                            ErrorType = "InvalidInput",
                            Message = "工作流 ID 格式无效",
                        },
                    ],
                };
            }

            workflowIdValue = wid;
        }

        var request = new ValidateWorkflowRequest
        {
            WorkflowId = workflowIdValue,
            Nodes = nodes,
            Connections = connections,
        };

        var result = await validationService.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// 确认草稿工作流，将其激活部署。草稿激活后即可执行。
    /// </summary>
    /// <param name="draftId">草稿工作流 ID（合法 Guid 格式）。</param>
    /// <returns>激活后的工作流 DTO 或结构化错误。</returns>
    [McpServerTool(Name = "confirm_workflow")]
    [Description("确认草稿工作流，将其激活部署。草稿激活后即可执行。")]
    public async Task<object> ConfirmWorkflow(
        [Description("草稿工作流 ID（合法 Guid 格式）。")] string draftId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draftId) || !Guid.TryParse(draftId, out var id))
        {
            return new { success = false, errorCode = "InvalidInput", message = "草稿 ID 格式无效" };
        }

        var workflow = await workflowService.ConfirmDraftAsync(id, cancellationToken).ConfigureAwait(false);
        if (workflow is null)
        {
            return new { success = false, errorCode = "NotFound", message = $"草稿 '{draftId}' 不存在" };
        }

        return workflow;
    }

    /// <summary>
    /// 执行已激活的工作流。返回执行 ID 和状态。执行失败时返回结构化反馈（含建议修复方案），供 AI 自纠。
    /// </summary>
    /// <param name="workflowId">工作流 ID（合法 Guid 格式）。</param>
    /// <param name="inputs">输入参数（可选）。</param>
    /// <param name="idempotencyKey">幂等键（可选）。</param>
    /// <returns>执行 DTO 或结构化错误。</returns>
    [McpServerTool(Name = "execute_workflow")]
    [Description("执行已激活的工作流。返回执行 ID 和状态。执行失败时返回结构化反馈（含建议修复方案），供 AI 自纠。")]
    public async Task<object> ExecuteWorkflow(
        [Description("工作流 ID（合法 Guid 格式）。")] string workflowId,
        [Description("输入参数（可选）。")] Dictionary<string, object>? inputs = null,
        [Description("幂等键（可选）。")] string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workflowId) || !Guid.TryParse(workflowId, out var id))
        {
            return new { success = false, errorCode = "InvalidInput", message = "工作流 ID 格式无效" };
        }

        try
        {
            var execution = await executionService.ExecuteAsync(id, idempotencyKey, cancellationToken, inputs)
                .ConfigureAwait(false);
            if (execution is null)
            {
                return new { success = false, errorCode = "NotFound", message = $"工作流 '{workflowId}' 不存在" };
            }

            return execution;
        }
        catch (BusinessException ex)
        {
            return new { success = false, errorCode = "ExecutionFailed", message = ex.Message };
        }
    }
}
