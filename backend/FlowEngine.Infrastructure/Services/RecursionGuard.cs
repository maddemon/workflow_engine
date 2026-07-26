using System.Collections.Concurrent;
using FlowEngine.Core.Abstractions;

namespace FlowEngine.Infrastructure.Services;

/// <summary>
/// 递归深度保护实现：按节点 ID 维护当前递归深度计数，超过上限时拒绝进入，
/// 防止子执行/嵌套调用的无限递归耗尽资源。
/// </summary>
public sealed class RecursionGuard : IRecursionGuard
{
    private readonly ConcurrentDictionary<string, int> _depths = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxDepth;

    /// <summary>构造递归保护。</summary>
    /// <param name="options">递归深度选项。</param>
    public RecursionGuard(RecursionGuardOptions options) => _maxDepth = options?.MaxDepth > 0 ? options.MaxDepth : 100;

    /// <summary>构造递归保护（直接指定最大深度）。</summary>
    /// <param name="maxDepth">最大递归深度。</param>
    public RecursionGuard(int maxDepth = 100) => _maxDepth = maxDepth > 0 ? maxDepth : 100;

    /// <inheritdoc />
    public bool TryEnter(string nodeId)
    {
        var depth = _depths.AddOrUpdate(nodeId, 1, (_, v) => v + 1);
        if (depth > _maxDepth)
        {
            // 回退本次越界计数，保持计数准确。
            _depths.TryUpdate(nodeId, depth - 1, depth);
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public void Exit(string nodeId)
    {
        _depths.AddOrUpdate(nodeId, 0, (_, v) => v > 0 ? v - 1 : 0);
    }
}
