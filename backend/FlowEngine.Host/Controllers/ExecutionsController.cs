using FlowEngine.Application.Dtos;
using FlowEngine.Application.Executions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Enums;
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

        return CreatedAtAction(nameof(Get), new { id = execution.Id }, execution);
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
            return NotFound(new
            {
                success = false,
                errorCode = "ExecutionNotFound",
                message = localizer["ExecutionNotFoundFormat", id],
            });
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
    /// 按工作流定义 ID 分页获取执行列表。
    /// </summary>
    [HttpGet("workflows/{workflowId:guid}/executions")]
    [AuthorizePermission(Scope.Execution, Operation.Read)]
    public async Task<ActionResult<PagedResult<ExecutionSummaryDto>>> GetByWorkflow(
        Guid workflowId,
        [FromQuery] Guid? projectId = null,
        [FromQuery] ExecutionStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await executionService.GetByWorkflowAsync(workflowId, projectId, status, page, pageSize, cancellationToken)
            .ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// 获取指定工作流当前运行中的执行（待执行/执行中），供前端实时跟踪。
    /// </summary>
    [HttpGet("workflows/{workflowId:guid}/executions/active")]
    [AuthorizePermission(Scope.Execution, Operation.Read)]
    public async Task<ActionResult<IReadOnlyCollection<ExecutionSummaryDto>>> GetActive(
        Guid workflowId,
        [FromQuery] Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await executionService.GetActiveAsync(workflowId, projectId, cancellationToken)
            .ConfigureAwait(false);
        return Ok(result);
    }
}
