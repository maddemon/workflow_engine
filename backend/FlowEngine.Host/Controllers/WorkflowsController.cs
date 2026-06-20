using FlowEngine.Application.Dtos;
using FlowEngine.Application.Workflows;
using Microsoft.AspNetCore.Mvc;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// 工作流 CRUD API。
/// </summary>
[ApiController]
[Route("api/v1/workflows")]
public class WorkflowsController(WorkflowService workflowService) : ControllerBase
{
    /// <summary>
    /// 获取所有工作流摘要列表。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<WorkflowSummaryDto>>> GetAll(
        CancellationToken cancellationToken)
    {
        var workflows = await workflowService.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return Ok(workflows);
    }

    /// <summary>
    /// 按 ID 获取最新版本工作流。
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkflowDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var workflow = await workflowService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (workflow is null)
        {
            return NotFound();
        }

        return Ok(workflow);
    }

    /// <summary>
    /// 创建工作流。
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<WorkflowDto>> Create(
        [FromBody] CreateWorkflowDto workflow,
        CancellationToken cancellationToken)
    {
        var result = await workflowService.CreateAsync(workflow, cancellationToken).ConfigureAwait(false);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    /// <summary>
    /// 更新工作流并递增版本号。
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WorkflowDto>> Update(
        Guid id,
        [FromBody] UpdateWorkflowDto workflow,
        CancellationToken cancellationToken)
    {
        var result = await workflowService.UpdateAsync(id, workflow, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    /// <summary>
    /// 删除工作流。
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await workflowService.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }

    /// <summary>
    /// 获取工作流的所有历史版本号。
    /// </summary>
    [HttpGet("{id:guid}/versions")]
    public async Task<ActionResult<IReadOnlyCollection<int>>> GetVersions(
        Guid id,
        CancellationToken cancellationToken)
    {
        var versions = await workflowService.GetVersionsAsync(id, cancellationToken).ConfigureAwait(false);
        return Ok(versions);
    }

    /// <summary>
    /// 按版本号获取工作流。
    /// </summary>
    [HttpGet("{id:guid}/versions/{version:int}")]
    public async Task<ActionResult<WorkflowDto>> GetVersion(
        Guid id,
        int version,
        CancellationToken cancellationToken)
    {
        var workflow = await workflowService.GetVersionAsync(id, version, cancellationToken)
            .ConfigureAwait(false);
        if (workflow is null)
        {
            return NotFound();
        }

        return Ok(workflow);
    }
}
