using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// 用户角色管理 API。
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize(Roles = "Admin")]
public class UsersController(UserRoleService userRoleService) : ControllerBase
{
    /// <summary>
    /// 查询用户角色列表。
    /// </summary>
    [HttpGet("{userId:guid}/roles")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetRoles(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var roles = await userRoleService.GetRolesAsync(userId, cancellationToken).ConfigureAwait(false);
        return Ok(roles);
    }

    /// <summary>
    /// 分配角色给指定用户。
    /// </summary>
    [HttpPost("{userId:guid}/roles")]
    public async Task<IActionResult> AssignRole(
        Guid userId,
        [FromBody] AssignRoleRequest request,
        CancellationToken cancellationToken)
    {
        var (success, error) = await userRoleService.AssignRoleAsync(userId, request, cancellationToken).ConfigureAwait(false);
        if (!success)
        {
            return this.BadRequestError(error);
        }

        return Ok(new { userId, role = request.Role });
    }

    /// <summary>
    /// 撤销指定用户的角色。
    /// </summary>
    [HttpDelete("{userId:guid}/roles/{role}")]
    public async Task<IActionResult> RevokeRole(
        Guid userId,
        string role,
        CancellationToken cancellationToken)
    {
        var (success, error) = await userRoleService.RevokeRoleAsync(userId, role, cancellationToken).ConfigureAwait(false);
        if (!success)
        {
            return this.BadRequestError(error);
        }

        return NoContent();
    }
}
