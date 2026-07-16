using FlowEngine.Application.Dtos;
using FlowEngine.Application.Executions;
using FlowEngine.Core.Authorization;
using FlowEngine.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// 执行 API。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1")]
public class ExecutionsController(
    ExecutionService executionService,
    IStringLocalizer<SharedResource> localizer) : ControllerBase
{
    /// <summary>
    /// 启动工作流执行。
    /// </summary>
    [HttpPost("workflows/{workflowId:guid}/execute")]
    [AuthorizePermission(Scope.Execution, Operation.Execute)]
    public async Task<ActionResult<ExecutionDto>> Execute(
        Guid workflowId,
        [FromHeader(Name = "X-Idempotency-Key")] string? idempotencyKey,
        [FromBody] ExecuteWorkflowDto? dto,
        CancellationToken cancellationToken)
    {
        var effectiveIdempotencyKey = dto?.IdempotencyKey ?? idempotencyKey;
        var execution = await executionService.ExecuteAsync(
            workflowId,
            effectiveIdempotencyKey,
            cancellationToken,
            dto?.Inputs).ConfigureAwait(false);
        if (execution is null)
        {
            return NotFound(new
            {
                success = false,
                errorCode = "WorkflowNotFound",
                message = localizer["WorkflowNotFoundFormat", workflowId],
            });
        }

        return Ok(execution);
    }

    /// <summary>
    /// 取消执行。
    /// </summary>
    [HttpPost("executions/{id:guid}/cancel")]
    [AuthorizePermission(Scope.Execution, Operation.Execute)]
    public async Task<ActionResult<ExecutionDto>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var (execution, conflict) = await executionService.CancelAsync(id, cancellationToken).ConfigureAwait(false);
        if (execution is null)
        {
            return NotFound();
        }

        if (conflict)
        {
            return Conflict(new
            {
                success = false,
                errorCode = "ExecutionCannotCancel",
                message = localizer["ExecutionCannotCancelFormat", id, execution.Status],
            });
        }

        return Ok(execution);
    }

    /// <summary>
    /// 按 ID 获取执行详情。
    /// </summary>
    [HttpGet("executions/{id:guid}")]
    [AuthorizePermission(Scope.Execution, Operation.Read)]
    public async Task<ActionResult<ExecutionDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var execution = await executionService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return this.OkOrNotFound(execution);
    }

    /// <summary>
    /// 按工作流定义 ID 获取执行列表。
    /// </summary>
    [HttpGet("workflows/{workflowId:guid}/executions")]
    [AuthorizePermission(Scope.Execution, Operation.Read)]
    public async Task<ActionResult<IReadOnlyCollection<ExecutionSummaryDto>>> GetByWorkflow(
        Guid workflowId,
        [FromQuery] Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var executions = await executionService.GetByWorkflowAsync(workflowId, projectId, cancellationToken)
            .ConfigureAwait(false);
        return Ok(executions);
    }
}
