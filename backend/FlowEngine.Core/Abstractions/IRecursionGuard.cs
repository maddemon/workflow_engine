namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 递归深度保护：限制单节点在工作流执行中的递归/嵌套调用深度，防止无限递归耗尽资源。
/// 按节点 ID 维护当前递归深度计数。
/// </summary>
public interface IRecursionGuard
{
    /// <summary>
    /// 尝试进入指定节点的递归层级；若未超过上限则递增深度并返回 true，否则返回 false（调用方应终止）。
    /// </summary>
    /// <param name="nodeId">节点实例 ID。</param>
    /// <returns>允许进入返回 true；超出上限返回 false。</returns>
    bool TryEnter(string nodeId);

    /// <summary>退出指定节点的递归层级，递减深度（不低于 0）。</summary>
    /// <param name="nodeId">节点实例 ID。</param>
    void Exit(string nodeId);
}
