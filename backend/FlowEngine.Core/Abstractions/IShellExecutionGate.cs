namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 判定当前请求上下文是否允许执行 <c>RunInShell=true</c> 的 Shell 命令（高危路径）。
/// 实现结合 <c>Shell:AllowShellExecution</c> 配置开关与当前用户角色（默认仅 Admin），
/// 由宿主层提供具体实现，运行时（<c>NodeExecutionContextFactory</c>）仅依赖此抽象。
/// </summary>
public interface IShellExecutionGate
{
    /// <summary>
    /// 判断 Shell 执行是否被授权（配置开启且当前用户具备允许的角色）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>授权则返回 <c>true</c>。</returns>
    Task<bool> IsShellExecutionAllowedAsync(CancellationToken cancellationToken = default);
}
