using FlowEngine.Application.Dtos;
using FlowEngine.Application.Executions;
using FlowEngine.Core.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// 执行 API。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1")]
public class ExecutionsController(ExecutionService executionService) : ControllerBase
{
    /// <summary>
    /// 启动工作流执行。
    /// </summary>
    [HttpPost("workflows/{workflowId:guid}/execute")]
    [AuthorizePermission(Scope.Execution, Operation.Execute)]
    public async Task<ActionResult<ExecutionDto>> Execute(
        Guid workflowId,
        [FromHeader(Name = "X-Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var execution = await executionService.ExecuteAsync(workflowId, idempotencyKey, cancellationToken).ConfigureAwait(false);
        if (execution is null)
        {
            return NotFound(new { message = $"工作流 '{workflowId}' 不存在。" });
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
        if (execution is null)
        {
            return NotFound();
        }

        return Ok(execution);
    }

    /// <summary>
    /// 按工作流定义 ID 获取执行列表。
    /// </summary>
    [HttpGet("workflows/{workflowId:guid}/executions")]
    [AuthorizePermission(Scope.Execution, Operation.Read)]
    public async Task<ActionResult<IReadOnlyCollection<ExecutionSummaryDto>>> GetByWorkflow(
        Guid workflowId,
        CancellationToken cancellationToken)
    {
        var executions = await executionService.GetByWorkflowAsync(workflowId, cancellationToken)
            .ConfigureAwait(false);
        return Ok(executions);
    }
}
