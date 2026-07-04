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

    /// <summary>
    /// 获取项目所有成员（已废弃）。
    /// </summary>
    [Obsolete("项目成员功能已废弃，项目仅用于分类。")]
    [HttpGet("{id:guid}/members")]
    [AuthorizePermission(Scope.Project, Operation.Read)]
    public async Task<ActionResult<IReadOnlyList<ProjectMemberDto>>> GetMembers(
        Guid id,
        CancellationToken cancellationToken)
    {
#pragma warning disable CS0618
        var result = await projectService.GetMembersAsync(id, cancellationToken).ConfigureAwait(false);
#pragma warning restore CS0618
        return Ok(result);
    }

    /// <summary>
    /// 添加项目成员（已废弃）。
    /// </summary>
    [Obsolete("项目成员功能已废弃，项目仅用于分类。")]
    [HttpPost("{id:guid}/members")]
    [AuthorizePermission(Scope.Project, Operation.Write)]
    public async Task<ActionResult<ProjectMemberDto>> AddMember(
        Guid id,
        [FromBody] AddProjectMemberDto dto,
        CancellationToken cancellationToken)
    {
#pragma warning disable CS0618
        var result = await projectService.AddMemberAsync(id, dto, cancellationToken).ConfigureAwait(false);
#pragma warning restore CS0618
        if (result is null)
        {
            return NotFound();
        }

        return CreatedAtAction(nameof(GetMembers), new { id }, result);
    }

    /// <summary>
    /// 更新项目成员角色（已废弃）。
    /// </summary>
    [Obsolete("项目成员功能已废弃，项目仅用于分类。")]
    [HttpPut("{id:guid}/members/{memberId:guid}")]
    [AuthorizePermission(Scope.Project, Operation.Write)]
    public async Task<ActionResult<ProjectMemberDto>> UpdateMemberRole(
        Guid id,
        Guid memberId,
        [FromBody] UpdateProjectMemberDto dto,
        CancellationToken cancellationToken)
    {
#pragma warning disable CS0618
        var result = await projectService.UpdateMemberRoleAsync(id, memberId, dto, cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore CS0618
        if (result is null)
        {
            return NotFound();
        }

        return Ok(result);
    }

    /// <summary>
    /// 移除项目成员（已废弃）。
    /// </summary>
    [Obsolete("项目成员功能已废弃，项目仅用于分类。")]
    [HttpDelete("{id:guid}/members/{memberId:guid}")]
    [AuthorizePermission(Scope.Project, Operation.Write)]
    public async Task<IActionResult> RemoveMember(
        Guid id,
        Guid memberId,
        CancellationToken cancellationToken)
    {
#pragma warning disable CS0618
        var removed = await projectService.RemoveMemberAsync(id, memberId, cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore CS0618
        if (!removed)
        {
            return NotFound();
        }

        return NoContent();
    }
}
