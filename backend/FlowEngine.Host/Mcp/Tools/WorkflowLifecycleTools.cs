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
    IExecutionService executionService,
    IWorkflowExecutionFeedbackService feedbackService)
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
    public async Task<object> ValidateWorkflow(
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

        bool hasWorkflowId = workflowIdValue.HasValue;
        bool hasDraft = (nodes?.Count ?? 0) > 0 || (connections?.Count ?? 0) > 0;

        if (hasWorkflowId == hasDraft)
        {
            return new McpToolError(
                "InvalidInput",
                "请提供 workflowId（校验已有工作流）或 nodes/connections（校验草稿），二者不可同时为空或同时传入。",
                true,
                "如需校验已有工作流，仅传 workflowId；如需校验草稿，仅传 nodes/connections。");
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
            return new McpToolError(
                "InvalidInput",
                "草稿 ID 格式无效",
                CanAutoFix: true,
                SuggestedFix: "请检查并修正输入参数");
        }

        var workflow = await workflowService.ConfirmDraftAsync(id, cancellationToken).ConfigureAwait(false);
        if (workflow is null)
        {
            return new McpToolError(
                "NotFound",
                $"草稿 '{draftId}' 不存在",
                CanAutoFix: false,
                SuggestedFix: "请确认 ID 正确或先创建/装配");
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
            return new McpToolError(
                "InvalidInput",
                "工作流 ID 格式无效",
                CanAutoFix: true,
                SuggestedFix: "请检查并修正输入参数");
        }

        try
        {
            var execution = await executionService.ExecuteAsync(id, idempotencyKey, cancellationToken, inputs)
                .ConfigureAwait(false);
            if (execution is null)
            {
                return new McpToolError(
                    "NotFound",
                    $"工作流 '{workflowId}' 不存在",
                    CanAutoFix: false,
                    SuggestedFix: "请确认 ID 正确或先创建/装配");
            }

            // 执行已触发；若存在失败节点记录，附上结构化反馈供 AI 自纠（设计文档 §5.4）。
            // 执行成功或暂无反馈记录时保持原有契约，仅返回 ExecutionDto。
            // 反馈读取属执行结果的「增值信息」，读取失败不应阻断执行结果返回，降级为仅返回 ExecutionDto。
            ExecutionFeedbackResult? feedback = null;
            try
            {
                feedback = await feedbackService.GetFeedbackAsync(execution.Id, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // 反馈读取异常（如数据库瞬时不可用）时静默降级，保证 execute_workflow 主路径可用。
            }

            if (feedback is not null && !feedback.Success)
            {
                return new ExecuteWorkflowResult(execution, feedback);
            }

            return execution;
        }
        catch (BusinessException ex)
        {
            return new McpToolError(
                "ExecutionFailed",
                ex.Message,
                CanAutoFix: true,
                SuggestedFix: "请根据错误信息调整工作流或输入参数");
        }
    }

    /// <summary>
    /// 拒绝 AI 生成的工作流草稿，写入拒绝理由。草稿保留以供 AI 拉取反馈。
    /// </summary>
    /// <param name="draftId">草稿工作流 ID（合法 Guid 格式）。</param>
    /// <param name="reason">拒绝理由，描述人类为什么拒绝此草稿。</param>
    /// <returns>拒绝后的工作流 DTO 或结构化错误。</returns>
    [McpServerTool(Name = "reject_draft")]
    [Description("拒绝 AI 生成的工作流草稿，写入拒绝理由。草稿保留以供 AI 拉取反馈。")]
    public async Task<object> RejectDraft(
        [Description("草稿工作流 ID（合法 Guid 格式）。")] string draftId,
        [Description("拒绝理由，描述人类为什么拒绝此草稿。")] string reason,
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

        if (string.IsNullOrWhiteSpace(reason))
        {
            return new McpToolError(
                "InvalidInput",
                "拒绝理由不能为空",
                CanAutoFix: false,
                SuggestedFix: "请填写拒绝理由");
        }

        var result = await workflowService.RejectDraftAsync(id, reason, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return new McpToolError(
                "NotFound",
                $"草稿 '{draftId}' 不存在",
                CanAutoFix: false,
                SuggestedFix: "请确认 ID 正确或先创建/装配");
        }

        return result;
    }
}
