namespace FlowEngine.Core.Authorization;

/// <summary>
/// 权限定义，表示某个角色在特定作用域下允许的操作集合。
/// </summary>
/// <param name="Role">角色。</param>
/// <param name="Scope">作用域。</param>
/// <param name="AllowedOperations">允许的操作集合。</param>
public sealed record Permission(Role Role, Scope Scope, IReadOnlySet<Operation> AllowedOperations);