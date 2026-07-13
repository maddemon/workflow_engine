using FlowEngine.Application.Dtos;
using FlowEngine.Application.Executions;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// AI 工作流装配/修改/校验/确认 API。
/// 独立于人工 CRUD 控制器，隔离 AI 专用语义。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/workflows")]
public class AiWorkflowsController(
    IWorkflowAssemblyService assemblyService,
    IWorkflowModificationService modificationService,
    WorkflowValidationService validationService,
    WorkflowExecutionFeedbackService feedbackService,
    WorkflowService workflowService) : ControllerBase
{
    /// <summary>
    /// 装配 AI 草稿为完整工作流。
    /// </summary>
    [HttpPost("assemble")]
    [AuthorizePermission(Scope.Workflow, Operation.Write)]
    public async Task<ActionResult<AssembleWorkflowResult>> Assemble(
        [FromBody] AssembleWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await assemblyService.AssembleAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return CreatedAtAction("Get", "Workflows", new { id = result.DraftId }, result);
        }
        catch (BusinessException ex)
        {
            return BadRequest(new
            {
                success = false,
                errorCode = "AssembleFailed",
                message = ex.Message,
            });
        }
    }

    /// <summary>
    /// 对已有工作流应用结构化修改。
    /// </summary>
    [HttpPost("{id:guid}/modify")]
    [AuthorizePermission(Scope.Workflow, Operation.Write)]
    public async Task<ActionResult<ModifyWorkflowResult>> Modify(
        Guid id,
        [FromBody] ModifyWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await modificationService.ModifyAsync(id, request, cancellationToken)
                .ConfigureAwait(false);
            return Ok(result);
        }
        catch (BusinessException ex)
        {
            return BadRequest(new
            {
                success = false,
                errorCode = "ModifyFailed",
                message = ex.Message,
            });
        }
    }

    /// <summary>
    /// 校验工作流定义。
    /// </summary>
    [HttpPost("validate")]
    [AuthorizePermission(Scope.Workflow, Operation.Read)]
    public async Task<ActionResult<ValidateWorkflowResult>> Validate(
        [FromBody] ValidateWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var result = await validationService.ValidateAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// 确认草稿（激活工作流）。
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    [AuthorizePermission(Scope.Workflow, Operation.Write)]
    public async Task<ActionResult<WorkflowDto>> ConfirmDraft(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await workflowService.ConfirmDraftAsync(id, cancellationToken)
            .ConfigureAwait(false);
        return this.OkOrNotFound(result);
    }

    /// <summary>
    /// 获取执行反馈。
    /// </summary>
    [HttpGet("{workflowId:guid}/executions/{executionId:guid}/feedback")]
    [AuthorizePermission(Scope.Execution, Operation.Read)]
    public async Task<ActionResult<ExecutionFeedbackResult>> GetFeedback(
        Guid workflowId,
        Guid executionId,
        CancellationToken cancellationToken)
    {
        var feedback = await feedbackService.GetFeedbackAsync(executionId, cancellationToken)
            .ConfigureAwait(false);
        if (feedback is null)
        {
            return NotFound(new
            {
                success = false,
                errorCode = "ExecutionNotFound",
                message = $"执行 '{executionId}' 不存在。",
            });
        }

        return Ok(feedback);
    }
}
