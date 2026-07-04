using FlowEngine.Core.Authorization;

namespace FlowEngine.Application.Authorization;

/// <summary>
/// RBAC 授权服务接口。
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// 检查指定角色集合是否在给定作用域下拥有某个操作的权限。
    /// </summary>
    bool HasPermission(IReadOnlyList<string> roles, Scope scope, Operation operation);

    /// <summary>
    /// 获取指定角色集合在某个操作下拥有权限的所有作用域。
    /// </summary>
    IReadOnlyList<string> GetAllowedScopes(IReadOnlyList<string> roles, Operation operation);
}
