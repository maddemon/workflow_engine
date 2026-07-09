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
        => (await DecideAsync(userId, ResourceKind.Workflow, workflowId, operation, ct).ConfigureAwait(false)) == AccessDecision.Allowed;

    /// <inheritdoc />
    public async Task<bool> CanAccessCredentialAsync(Guid userId, Guid credentialId, Operation operation, CancellationToken ct = default)
        => (await DecideAsync(userId, ResourceKind.Credential, credentialId, operation, ct).ConfigureAwait(false)) == AccessDecision.Allowed;

    /// <inheritdoc />
    public async Task<bool> CanAccessExecutionAsync(Guid userId, Guid executionId, Operation operation, CancellationToken ct = default)
        => (await DecideAsync(userId, ResourceKind.Execution, executionId, operation, ct).ConfigureAwait(false)) == AccessDecision.Allowed;

    /// <inheritdoc />
    public async Task<bool> CanAccessTriggerAsync(Guid userId, Guid triggerId, Operation operation, CancellationToken ct = default)
        => (await DecideAsync(userId, ResourceKind.Trigger, triggerId, operation, ct).ConfigureAwait(false)) == AccessDecision.Allowed;

    /// <inheritdoc />
    public async Task<AccessDecision> DecideAsync(Guid userId, ResourceKind kind, Guid resourceId, Operation operation, CancellationToken ct = default)
    {
        var (scope, projectId) = await ResolveProjectAsync(kind, resourceId, ct).ConfigureAwait(false);
        var roles = await GetUserRolesAsync(userId, ct).ConfigureAwait(false);

        if (!CheckRoleAccess(roles, scope, operation))
        {
            return AccessDecision.DeniedByRole;
        }

        if (IsAdmin(roles))
        {
            return AccessDecision.Allowed;
        }

        if (!projectId.HasValue)
        {
            // 未关联/未找到项目的资源：非 Admin 用户不应默认获得访问权，避免水平越权。
            return AccessDecision.DeniedByOwnership;
        }

        return await OwnsProjectAsync(userId, projectId.Value, ct).ConfigureAwait(false)
            ? AccessDecision.Allowed
            : AccessDecision.DeniedByOwnership;
    }

    /// <summary>
    /// 按资源类型解析其所属项目 ID 与用于角色判定的作用域。
    /// 文件继承所属项目权限，故作用域取 Project（审计 resourceType 仍为 File）。
    /// </summary>
    private async Task<(Scope scope, Guid? projectId)> ResolveProjectAsync(ResourceKind kind, Guid resourceId, CancellationToken ct)
    {
        return kind switch
        {
            ResourceKind.Workflow => (Scope.Workflow, await dbContext.Workflows.AsNoTracking().Where(w => w.Id == resourceId && !w.Deleted).Select(w => (Guid?)w.ProjectId).FirstOrDefaultAsync(ct).ConfigureAwait(false)),
            ResourceKind.Credential => (Scope.Credential, await dbContext.Credentials.AsNoTracking().Where(c => c.Id == resourceId && !c.Deleted).Select(c => (Guid?)c.ProjectId).FirstOrDefaultAsync(ct).ConfigureAwait(false)),
            ResourceKind.Execution => (Scope.Execution, await dbContext.ExecutionRecords.AsNoTracking().Where(e => e.Id == resourceId && !e.Deleted).Select(e => (Guid?)e.ProjectId).FirstOrDefaultAsync(ct).ConfigureAwait(false)),
            ResourceKind.Trigger => (Scope.Trigger, await dbContext.Triggers.AsNoTracking().Where(t => t.Id == resourceId && !t.Deleted).Select(t => (Guid?)t.ProjectId).FirstOrDefaultAsync(ct).ConfigureAwait(false)),
            ResourceKind.Project => (Scope.Project, resourceId),
            ResourceKind.File => (Scope.Project, await dbContext.StoredFiles.AsNoTracking().Where(f => f.Id == resourceId && !f.Deleted).Select(f => (Guid?)f.ProjectId).FirstOrDefaultAsync(ct).ConfigureAwait(false)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
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
