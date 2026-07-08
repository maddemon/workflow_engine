using FlowEngine.Application.Identity;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Authorization;

/// <summary>
/// 资源级授权服务实现，基于用户角色、项目所有权与权限矩阵判定资源访问权限。
/// Admin 拥有全部资源访问权；其他用户只能访问其拥有的项目内的资源。
/// </summary>
public sealed class ResourceAuthorizationService(FlowEngineDbContext dbContext, IAuthorizationService authorizationService) : IResourceAuthorizationService
{
    // P4：请求级角色缓存（服务为 Scoped，等价于请求级），避免同一请求内反复查库。
    private readonly Dictionary<Guid, IReadOnlyList<string>> _roleCache = new();

    /// <inheritdoc />
    public async Task<bool> CanAccessWorkflowAsync(Guid userId, Guid workflowId, Operation operation, CancellationToken ct = default)
    {
        var roles = await GetUserRolesAsync(userId, ct).ConfigureAwait(false);
        if (!CheckRoleAccess(roles, Scope.Workflow, operation))
        {
            return false;
        }

        if (IsAdmin(roles))
        {
            return true;
        }

        var projectId = await dbContext.Workflows
            .AsNoTracking()
            .Where(w => w.Id == workflowId && !w.Deleted)
            .Select(w => w.ProjectId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return await OwnsProjectAsync(userId, projectId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> CanAccessCredentialAsync(Guid userId, Guid credentialId, Operation operation, CancellationToken ct = default)
    {
        var roles = await GetUserRolesAsync(userId, ct).ConfigureAwait(false);
        if (!CheckRoleAccess(roles, Scope.Credential, operation))
        {
            return false;
        }

        if (IsAdmin(roles))
        {
            return true;
        }

        var projectId = await dbContext.Credentials
            .AsNoTracking()
            .Where(c => c.Id == credentialId && !c.Deleted)
            .Select(c => c.ProjectId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return await OwnsProjectAsync(userId, projectId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> CanAccessExecutionAsync(Guid userId, Guid executionId, Operation operation, CancellationToken ct = default)
    {
        var roles = await GetUserRolesAsync(userId, ct).ConfigureAwait(false);
        if (!CheckRoleAccess(roles, Scope.Execution, operation))
        {
            return false;
        }

        if (IsAdmin(roles))
        {
            return true;
        }

        var projectId = await dbContext.ExecutionRecords
            .AsNoTracking()
            .Where(e => e.Id == executionId && !e.Deleted)
            .Select(e => e.ProjectId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return await OwnsProjectAsync(userId, projectId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> CanAccessTriggerAsync(Guid userId, Guid triggerId, Operation operation, CancellationToken ct = default)
    {
        var roles = await GetUserRolesAsync(userId, ct).ConfigureAwait(false);
        if (!CheckRoleAccess(roles, Scope.Trigger, operation))
        {
            return false;
        }

        if (IsAdmin(roles))
        {
            return true;
        }

        var projectId = await dbContext.Triggers
            .AsNoTracking()
            .Where(t => t.Id == triggerId && !t.Deleted)
            .Select(t => t.ProjectId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return await OwnsProjectAsync(userId, projectId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool ShouldMaskCredentialValues(IReadOnlyList<string> roles)
    {
        return roles.Any(r => string.Equals(r, RoleConstants.Viewer, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<string>> GetUserRolesAsync(Guid userId, CancellationToken ct)
    {
        if (_roleCache.TryGetValue(userId, out var cached))
        {
            return cached;
        }

        var userRoles = await dbContext.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId && !ur.Deleted)
            .Select(ur => ur.Role)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        _roleCache[userId] = userRoles;
        return userRoles;
    }

    private bool CheckRoleAccess(IReadOnlyList<string> roles, Scope scope, Operation operation)
    {
        if (roles.Count == 0)
        {
            return false;
        }

        return authorizationService.HasPermission(roles, scope, operation);
    }

    /// <inheritdoc />
    public async Task<bool> CanAccessProjectAsync(Guid userId, Guid projectId, Operation operation, CancellationToken ct = default)
    {
        var roles = await GetUserRolesAsync(userId, ct).ConfigureAwait(false);
        if (!CheckRoleAccess(roles, Scope.Project, operation))
        {
            return false;
        }

        if (IsAdmin(roles))
        {
            return true;
        }

        return await OwnsProjectAsync(userId, projectId, ct).ConfigureAwait(false);
    }

    private static bool IsAdmin(IReadOnlyList<string> roles)
    {
        return roles.Any(r => string.Equals(r, RoleConstants.Admin, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> OwnsProjectAsync(Guid userId, Guid? projectId, CancellationToken ct)
    {
        if (!projectId.HasValue)
        {
            // 未关联项目的资源：非 Admin 用户不应默认获得访问权，避免水平越权。
            return false;
        }

        var project = await dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId.Value && !p.Deleted, ct)
            .ConfigureAwait(false);

        return project is not null && project.CreatedBy == userId;
    }
}
