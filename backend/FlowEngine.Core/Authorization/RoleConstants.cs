namespace FlowEngine.Core.Authorization;

/// <summary>
/// 角色名称常量，与 <see cref="Role"/> 枚举对应。
/// 用于 IUserContext.Roles 中的字符串角色名比较，消除硬编码。
/// </summary>
public static class RoleConstants
{
    public const string Admin = "Admin";
    public const string Editor = "Editor";
    public const string Viewer = "Viewer";
}
