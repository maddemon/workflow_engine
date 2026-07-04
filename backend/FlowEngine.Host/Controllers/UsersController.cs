using FlowEngine.Application.Dtos;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// 用户角色管理 API。
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize(Roles = "Admin")]
public class UsersController(FlowEngineDbContext dbContext) : ControllerBase
{
    /// <summary>
    /// 查询用户角色列表。
    /// </summary>
    [HttpGet("{userId:guid}/roles")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetRoles(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var roles = await dbContext.UserRoles
            .Where(ur => ur.UserId == userId && !ur.Deleted)
            .Select(ur => ur.Role)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

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
        if (string.IsNullOrWhiteSpace(request?.Role)
            || !Enum.TryParse<Role>(request.Role, ignoreCase: true, out var parsedRole))
        {
            return BadRequest(new { error = "BadRequest", message = "无效的角色。" });
        }

        var normalizedRole = parsedRole.ToString();

        var exists = await dbContext.UserRoles
            .AnyAsync(ur => ur.UserId == userId && ur.Role == normalizedRole && !ur.Deleted, cancellationToken)
            .ConfigureAwait(false);
        if (exists)
        {
            return Conflict(new { error = "Conflict", message = $"用户已拥有角色 '{normalizedRole}'。" });
        }

        dbContext.UserRoles.Add(new UserRole
        {
            UserId = userId,
            Role = normalizedRole,
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new { userId, role = normalizedRole });
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
        if (string.IsNullOrWhiteSpace(role)
            || !Enum.TryParse<Role>(role, ignoreCase: true, out var parsedRole))
        {
            return BadRequest(new { error = "BadRequest", message = "无效的角色。" });
        }

        var normalizedRole = parsedRole.ToString();

        var userRole = await dbContext.UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.Role == normalizedRole && !ur.Deleted, cancellationToken)
            .ConfigureAwait(false);
        if (userRole is null)
        {
            return NotFound(new { error = "NotFound", message = $"用户未拥有角色 '{normalizedRole}'。" });
        }

        userRole.Deleted = true;
        userRole.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return NoContent();
    }
}
