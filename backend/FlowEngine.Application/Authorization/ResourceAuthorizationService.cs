using FlowEngine.Application.Identity;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Authorization;

/// <summary>
/// 资源级授权服务实现，基于用户角色与权限矩阵判定资源访问权限。
/// </summary>
public sealed class ResourceAuthorizationService(FlowEngineDbContext dbContext, IAuthorizationService authorizationService) : IResourceAuthorizationService
{
    /// <inheritdoc />
    public async Task<bool> CanAccessWorkflowAsync(Guid userId, Guid workflowId, Operation operation, CancellationToken ct = default)
    {
        var roles = await GetUserRolesAsync(userId, ct).ConfigureAwait(false);
        return CheckAccess(roles, Scope.Workflow, operation);
    }

    /// <inheritdoc />
    public async Task<bool> CanAccessCredentialAsync(Guid userId, Guid credentialId, Operation operation, CancellationToken ct = default)
    {
        var roles = await GetUserRolesAsync(userId, ct).ConfigureAwait(false);
        return CheckAccess(roles, Scope.Credential, operation);
    }

    /// <inheritdoc />
    public async Task<bool> CanAccessExecutionAsync(Guid userId, Guid executionId, Operation operation, CancellationToken ct = default)
    {
        var roles = await GetUserRolesAsync(userId, ct).ConfigureAwait(false);
        return CheckAccess(roles, Scope.Execution, operation);
    }

    /// <inheritdoc />
    public async Task<bool> CanAccessTriggerAsync(Guid userId, Guid triggerId, Operation operation, CancellationToken ct = default)
    {
        var roles = await GetUserRolesAsync(userId, ct).ConfigureAwait(false);
        return CheckAccess(roles, Scope.Trigger, operation);
    }

    /// <inheritdoc />
    public bool ShouldMaskCredentialValues(IReadOnlyList<string> roles)
    {
        return roles.Any(r => string.Equals(r, RoleConstants.Viewer, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<string>> GetUserRolesAsync(Guid userId, CancellationToken ct)
    {
        var userRoles = await dbContext.UserRoles
            .Where(ur => ur.UserId == userId && !ur.Deleted)
            .Select(ur => ur.Role)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return userRoles;
    }

    private bool CheckAccess(IReadOnlyList<string> roles, Scope scope, Operation operation)
    {
        if (roles.Count == 0)
        {
            return false;
        }

        return authorizationService.HasPermission(roles, scope, operation);
    }
}
