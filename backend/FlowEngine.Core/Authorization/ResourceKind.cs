namespace FlowEngine.Core.Authorization;

/// <summary>
/// 受保护资源类型，用于 AuthorizationGuard 统一入口。
/// 数值与 Scope 对齐（File 除外，文件继承项目权限，详见 ResourceAuthorizationService）。
/// </summary>
public enum ResourceKind
{
    /// <summary>工作流。</summary>
    Workflow = 0,

    /// <summary>凭据。</summary>
    Credential = 1,

    /// <summary>执行记录。</summary>
    Execution = 2,

    /// <summary>触发器。</summary>
    Trigger = 3,

    /// <summary>项目。</summary>
    Project = 4,

    /// <summary>文件（继承所属项目权限）。</summary>
    File = 5,
}
