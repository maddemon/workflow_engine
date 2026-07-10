using FlowEngine.Application.Dtos;
using FlowEngine.Application.Projects;
using FlowEngine.Core.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// 项目 CRUD API。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/projects")]
public class ProjectsController(ProjectService projectService) : ControllerBase
{
    /// <summary>
    /// 获取当前用户所属的所有项目。
    /// </summary>
    [HttpGet]
    [AuthorizePermission(Scope.Project, Operation.Read)]
    public async Task<ActionResult<IReadOnlyList<ProjectDto>>> GetAll(CancellationToken cancellationToken)
    {
        var result = await projectService.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// 按 ID 获取项目。
    /// </summary>
    [HttpGet("{id:guid}")]
    [AuthorizePermission(Scope.Project, Operation.Read)]
    public async Task<ActionResult<ProjectDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var project = await projectService.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            return NotFound();
        }

        return Ok(project);
    }

    /// <summary>
    /// 创建项目。
    /// </summary>
    [HttpPost]
    [AuthorizePermission(Scope.Project, Operation.Write)]
    public async Task<ActionResult<ProjectDto>> Create(
        [FromBody] CreateProjectDto dto,
        CancellationToken cancellationToken)
    {
        var result = await projectService.CreateAsync(dto, cancellationToken).ConfigureAwait(false);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// 更新项目。
    /// </summary>
    [HttpPut("{id:guid}")]
    [AuthorizePermission(Scope.Project, Operation.Write)]
    public async Task<ActionResult<ProjectDto>> Update(
        Guid id,
        [FromBody] UpdateProjectDto dto,
        CancellationToken cancellationToken)
    {
        var result = await projectService.UpdateAsync(id, dto, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    /// <summary>
    /// 删除项目。
    /// </summary>
    [HttpDelete("{id:guid}")]
    [AuthorizePermission(Scope.Project, Operation.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await projectService.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }

}
