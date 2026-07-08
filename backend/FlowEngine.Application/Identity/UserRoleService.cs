using FlowEngine.Application.Dtos;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Identity;

/// <summary>
/// 用户角色管理应用服务，封装角色查询/分配/撤销逻辑（A2），避免 Controller 直接依赖 DbContext。
/// </summary>
public interface IUserRoleService
{
    /// <summary>获取用户角色列表。</summary>
    Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>分配角色，返回是否成功与错误信息。</summary>
    Task<(bool Success, string? Error)> AssignRoleAsync(Guid userId, AssignRoleRequest request, CancellationToken cancellationToken = default);

    /// <summary>撤销角色，返回是否成功与错误信息。</summary>
    Task<(bool Success, string? Error)> RevokeRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class UserRoleService(FlowEngineDbContext dbContext) : IUserRoleService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await dbContext.UserRoles
            .Where(ur => ur.UserId == userId && !ur.Deleted)
            .Select(ur => ur.Role)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<(bool Success, string? Error)> AssignRoleAsync(
        Guid userId,
        AssignRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Role)
            || !Enum.TryParse<Role>(request.Role, ignoreCase: true, out var parsedRole))
        {
            return (false, "无效的角色。");
        }

        var normalizedRole = parsedRole.ToString();

        var exists = await dbContext.UserRoles
            .AnyAsync(ur => ur.UserId == userId && ur.Role == normalizedRole && !ur.Deleted, cancellationToken)
            .ConfigureAwait(false);
        if (exists)
        {
            return (false, $"用户已拥有角色 '{normalizedRole}'。");
        }

        dbContext.UserRoles.Add(new UserRole
        {
            UserId = userId,
            Role = normalizedRole
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (true, null);
    }

    /// <inheritdoc />
    public async Task<(bool Success, string? Error)> RevokeRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(role)
            || !Enum.TryParse<Role>(role, ignoreCase: true, out var parsedRole))
        {
            return (false, "无效的角色。");
        }

        var normalizedRole = parsedRole.ToString();

        var userRole = await dbContext.UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.Role == normalizedRole && !ur.Deleted, cancellationToken)
            .ConfigureAwait(false);
        if (userRole is null)
        {
            return (false, $"用户未拥有角色 '{normalizedRole}'。");
        }

        userRole.Deleted = true;
        userRole.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (true, null);
    }
}
