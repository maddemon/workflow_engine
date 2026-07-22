using System.Collections.Generic;
using FlowEngine.Core.Entities;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 对连接图做 DFS，标记回边（指向 DFS 递归栈中祖先节点的边）。
/// 回边即环路的一部分，用于区分「节点被环路回边重新激活」（应复用上下文）
/// 与「节点收到新上游输入」（应重置上下文）。
/// 复杂度 O(V + E)，每个执行仅计算一次。
/// </summary>
internal static class CycleDetector
{
    /// <summary>
    /// 计算连接图中的回边集合。返回的连接四元组键与 <see cref="Connection"/> 字段一致，
    /// 供 <see cref="WorkflowSchedulerKernel"/> 通过 <c>session.FeedbackEdgeKeys.Contains(...)</c> 判定。
    /// </summary>
    public static IReadOnlySet<(string SourceNodeId, string? SourcePortName, string TargetNodeId, string? TargetPortName)> ComputeBackEdges(
        IEnumerable<Connection> connections)
    {
        var adjacency = new Dictionary<string, List<Connection>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in connections)
        {
            if (!adjacency.TryGetValue(c.SourceNodeId, out var list))
            {
                list = [];
                adjacency[c.SourceNodeId] = list;
            }

            list.Add(c);
        }

        var backEdges = new HashSet<(string, string?, string, string?)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var onStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in adjacency.Keys)
        {
            if (!visited.Contains(node))
            {
                Dfs(node, adjacency, visited, onStack, backEdges);
            }
        }

        return backEdges;
    }

    private static void Dfs(
        string node,
        Dictionary<string, List<Connection>> adjacency,
        HashSet<string> visited,
        HashSet<string> onStack,
        HashSet<(string, string?, string, string?)> backEdges)
    {
        visited.Add(node);
        onStack.Add(node);

        if (adjacency.TryGetValue(node, out var edges))
        {
            foreach (var c in edges)
            {
                if (!visited.Contains(c.TargetNodeId))
                {
                    Dfs(c.TargetNodeId, adjacency, visited, onStack, backEdges);
                }
                else if (onStack.Contains(c.TargetNodeId))
                {
                    backEdges.Add((c.SourceNodeId, c.SourcePortName, c.TargetNodeId, c.TargetPortName));
                }
            }
        }

        onStack.Remove(node);
    }
}
