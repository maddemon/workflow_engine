namespace FlowEngine.Core.Authorization;

/// <summary>
/// RBAC 权限授权特性，标注在 Controller 或 Action 上指定所需的作用域和操作。
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class AuthorizePermissionAttribute : Attribute
{
    /// <summary>
    /// 所需权限作用域。
    /// </summary>
    public Scope Scope { get; }

    /// <summary>
    /// 所需操作类型。
    /// </summary>
    public Operation Operation { get; }

    /// <summary>
    /// 初始化 <see cref="AuthorizePermissionAttribute"/> 实例。
    /// </summary>
    /// <param name="scope">作用域。</param>
    /// <param name="operation">操作类型。</param>
    public AuthorizePermissionAttribute(Scope scope, Operation operation)
    {
        Scope = scope;
        Operation = operation;
    }
}
