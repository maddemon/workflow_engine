using FlowEngine.Core.Authorization;

namespace FlowEngine.Application.Authorization;

/// <summary>
/// 统一授权守卫：封装认证检查、资源/作用域授权，并强制「拒绝必审计」不变量。
/// 用于替代各服务中重复的「userId 判空 + CanAccess + 审计 + 抛异常」样板，
/// 消除因手写遗漏导致的审计漏记（如历史版本读取、角色级拒绝路径）。
/// </summary>
public interface IAuthorizationGuard
{
    /// <summary>
    /// 校验当前用户对指定资源的操作权限。拒绝时自动写审计并抛 <see cref="PermissionDeniedException"/>(403)。
    /// </summary>
    Task RequireAccessAsync(ResourceKind kind, Guid resourceId, Operation operation, CancellationToken ct = default);

    /// <summary>
    /// 校验当前用户是否拥有指定作用域的操作权限（角色级）。拒绝时自动写审计并抛 <see cref="PermissionDeniedException"/>(403)。
    /// </summary>
    Task RequireScopeAsync(Scope scope, Operation operation, CancellationToken ct = default);

    /// <summary>
    /// 校验当前用户是否为 Admin。否则自动写审计并抛 <see cref="PermissionDeniedException"/>(403)。
    /// </summary>
    Task RequireAdminAsync(Operation operation, CancellationToken ct = default);
}
