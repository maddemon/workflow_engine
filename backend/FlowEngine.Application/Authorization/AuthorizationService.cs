using FlowEngine.Core.Authorization;

namespace FlowEngine.Application.Authorization;

/// <summary>
/// RBAC 授权服务实现，基于 PermissionMapping 静态权限矩阵。
/// </summary>
public sealed class AuthorizationService : IAuthorizationService
{
    /// <inheritdoc />
    public bool HasPermission(IReadOnlyList<string> roles, Scope scope, Operation operation)
    {
        foreach (var roleStr in roles)
        {
            if (Enum.TryParse<Role>(roleStr, ignoreCase: true, out var role)
                && PermissionMapping.HasPermission(role, scope, operation))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAllowedScopes(IReadOnlyList<string> roles, Operation operation)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var roleStr in roles)
        {
            if (!Enum.TryParse<Role>(roleStr, ignoreCase: true, out var role))
            {
                continue;
            }

            foreach (Scope scope in Enum.GetValues<Scope>())
            {
                if (PermissionMapping.HasPermission(role, scope, operation))
                {
                    allowed.Add(scope.ToString());
                }
            }
        }

        return allowed.ToList();
    }
}
