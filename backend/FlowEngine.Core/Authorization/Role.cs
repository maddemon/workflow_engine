using System.ComponentModel;

namespace FlowEngine.Core.Authorization;

/// <summary>
/// 用户角色枚举。
/// </summary>
public enum Role
{
    /// <summary>
    /// 管理员。
    /// </summary>
    [Description("管理员")]
    Admin = 0,

    /// <summary>
    /// 编辑者。
    /// </summary>
    [Description("编辑者")]
    Editor = 1,

    /// <summary>
    /// 查看者。
    /// </summary>
    [Description("查看者")]
    Viewer = 2
}