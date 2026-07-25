namespace FlowEngine.Application.Security;

/// <summary>
/// Shell 节点执行相关配置（SEC-1）。
/// </summary>
public sealed class ShellOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "Shell";

    /// <summary>
    /// 是否允许 <c>RunInShell=true</c> 的 Shell 命令经 shell 解释器执行。
    /// 默认 <c>false</c>（安全默认）：即便具备管理员角色，未显式开启此开关也禁止 Shell 执行。
    /// </summary>
    public bool AllowShellExecution { get; set; }

    /// <summary>
    /// 允许执行 Shell 的角色列表（大小写不敏感）。
    /// 默认仅 <c>Admin</c>。LLM/Agent 驱动的命令无论角色如何默认禁止（见 <see cref="Core.Entities.NodeExecutionContext.IsAgentInvocation"/>）。
    /// </summary>
    public IReadOnlyList<string> AllowedRoles { get; set; } = ["Admin"];
}
