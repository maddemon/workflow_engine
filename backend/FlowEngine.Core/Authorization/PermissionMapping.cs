using System.ComponentModel;
using System.Reflection;

namespace FlowEngine.Core.Authorization;

/// <summary>
/// 权限映射静态类，提供默认权限矩阵和权限查询方法。
/// </summary>
public static class PermissionMapping
{
    private static readonly IReadOnlyDictionary<Role, IReadOnlyDictionary<Scope, IReadOnlySet<Operation>>> DefaultPermissions =
        new Dictionary<Role, IReadOnlyDictionary<Scope, IReadOnlySet<Operation>>>
        {
            [Role.Admin] = CreateAdminPermissions(),
            [Role.Editor] = CreateEditorPermissions(),
            [Role.Viewer] = CreateViewerPermissions()
        };

    /// <summary>
    /// 检查指定角色在特定作用域下是否有执行某个操作的权限。
    /// </summary>
    /// <param name="role">角色。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="operation">操作类型。</param>
    /// <returns>如果有权限返回 true，否则返回 false。</returns>
    public static bool HasPermission(Role role, Scope scope, Operation operation)
    {
        if (!DefaultPermissions.TryGetValue(role, out var scopePermissions))
        {
            return false;
        }

        if (!scopePermissions.TryGetValue(scope, out var operations))
        {
            return false;
        }

        return operations.Contains(operation);
    }

    /// <summary>
    /// 获取指定角色的所有权限定义。
    /// </summary>
    /// <param name="role">角色。</param>
    /// <returns>该角色的权限列表。</returns>
    public static IReadOnlyList<Permission> GetPermissions(Role role)
    {
        if (!DefaultPermissions.TryGetValue(role, out var scopePermissions))
        {
            return Array.Empty<Permission>();
        }

        return scopePermissions
            .Select(kvp => new Permission(role, kvp.Key, kvp.Value))
            .ToList();
    }

    private static IReadOnlyDictionary<Scope, IReadOnlySet<Operation>> CreateAdminPermissions()
    {
        var allOperations = Enum.GetValues<Operation>().Cast<Operation>().ToHashSet();
        var permissions = new Dictionary<Scope, IReadOnlySet<Operation>>();

        foreach (Scope scope in Enum.GetValues<Scope>())
        {
            permissions[scope] = allOperations;
        }

        return permissions;
    }

    private static IReadOnlyDictionary<Scope, IReadOnlySet<Operation>> CreateEditorPermissions()
    {
        return new Dictionary<Scope, IReadOnlySet<Operation>>
        {
            [Scope.Workflow] = new HashSet<Operation> { Operation.Read, Operation.Write, Operation.Execute },
            [Scope.Credential] = new HashSet<Operation> { Operation.Read, Operation.Write },
            [Scope.Execution] = new HashSet<Operation> { Operation.Read, Operation.Execute },
            [Scope.Trigger] = new HashSet<Operation> { Operation.Read, Operation.Write },
            [Scope.Project] = new HashSet<Operation> { Operation.Read, Operation.Write },
            [Scope.User] = new HashSet<Operation> { Operation.Read }
        };
    }

    private static IReadOnlyDictionary<Scope, IReadOnlySet<Operation>> CreateViewerPermissions()
    {
        var readOnly = new HashSet<Operation> { Operation.Read };
        var permissions = new Dictionary<Scope, IReadOnlySet<Operation>>();

        foreach (Scope scope in Enum.GetValues<Scope>())
        {
            permissions[scope] = readOnly;
        }

        return permissions;
    }
}