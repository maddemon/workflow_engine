using System.ComponentModel;

namespace FlowEngine.Core.Authorization;

/// <summary>
/// 权限作用域枚举。
/// </summary>
public enum Scope
{
    /// <summary>
    /// 工作流。
    /// </summary>
    [Description("工作流")]
    Workflow = 0,

    /// <summary>
    /// 凭据。
    /// </summary>
    [Description("凭据")]
    Credential = 1,

    /// <summary>
    /// 执行。
    /// </summary>
    [Description("执行")]
    Execution = 2,

    /// <summary>
    /// 触发器。
    /// </summary>
    [Description("触发器")]
    Trigger = 3,

    /// <summary>
    /// 项目。
    /// </summary>
    [Description("项目")]
    Project = 4,

    /// <summary>
    /// 用户。
    /// </summary>
    [Description("用户")]
    User = 5,

    /// <summary>
    /// 文件。
    /// </summary>
    [Description("文件")]
    File = 6
}