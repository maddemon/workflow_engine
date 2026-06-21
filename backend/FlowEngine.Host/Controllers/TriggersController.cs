using FlowEngine.Application.Dtos;
using FlowEngine.Application.Triggers;
using Microsoft.AspNetCore.Mvc;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// 触发器 CRUD API。
/// </summary>
[ApiController]
[Route("api/v1/triggers")]
public class TriggersController(TriggerService triggerService) : ControllerBase
{
    /// <summary>
    /// 按工作流定义 ID 获取触发器列表。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<TriggerDto>>> GetByWorkflowDefinitionId(
        [FromQuery] Guid workflowDefinitionId,
        CancellationToken cancellationToken)
    {
        var triggers = await triggerService
            .GetByWorkflowDefinitionIdAsync(workflowDefinitionId, cancellationToken)
            .ConfigureAwait(false);
        return Ok(triggers);
    }

    /// <summary>
    /// 按 ID 获取触发器。
    /// </summary>
    [HttpGet("{id:guid}")]
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
