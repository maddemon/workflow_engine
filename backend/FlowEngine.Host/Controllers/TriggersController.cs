using FlowEngine.Application.Dtos;
using FlowEngine.Application.Triggers;
using FlowEngine.Core.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// 触发器 CRUD API。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/triggers")]
public class TriggersController(TriggerService triggerService) : ControllerBase
{
    /// <summary>
    /// 获取触发器列表。不传 workflowDefinitionId 时返回当前用户可见的所有触发器。
    /// </summary>
    [HttpGet]
    [AuthorizePermission(Scope.Trigger, Operation.Read)]
    public async Task<ActionResult<IReadOnlyCollection<TriggerDto>>> GetTriggers(
        [FromQuery] Guid? workflowDefinitionId,
        [FromQuery] Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        if (workflowDefinitionId is { } wfId)
        {
            var byWorkflow = await triggerService
                .GetByWorkflowDefinitionIdAsync(wfId, cancellationToken)
                .ConfigureAwait(false);
            return Ok(byWorkflow);
        }

        var all = await triggerService
            .GetAllForUserAsync(projectId, cancellationToken)
            .ConfigureAwait(false);
        return Ok(all);
    }

    /// <summary>
    /// 按 ID 获取触发器。
    /// </summary>
    [HttpGet("{id:guid}")]
    [AuthorizePermission(Scope.Trigger, Operation.Read)]
    public async Task<ActionResult<TriggerDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var trigger = await triggerService.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (trigger is null)
        {
            return NotFound();
        }

        return Ok(trigger);
    }

    /// <summary>
    /// 创建触发器。
    /// </summary>
    [HttpPost]
    [AuthorizePermission(Scope.Trigger, Operation.Write)]
    public async Task<ActionResult<TriggerDto>> Create(
        [FromBody] CreateTriggerDto dto,
        CancellationToken cancellationToken)
    {
        var result = await triggerService.CreateAsync(dto, cancellationToken).ConfigureAwait(false);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// 更新触发器。
    /// </summary>
    [HttpPut("{id:guid}")]
    [AuthorizePermission(Scope.Trigger, Operation.Write)]
    public async Task<ActionResult<TriggerDto>> Update(
        Guid id,
        [FromBody] UpdateTriggerDto dto,
        CancellationToken cancellationToken)
    {
        var result = await triggerService.UpdateAsync(id, dto, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    /// <summary>
    /// 删除触发器。
    /// </summary>
    [HttpDelete("{id:guid}")]
    [AuthorizePermission(Scope.Trigger, Operation.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await triggerService.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }
}
