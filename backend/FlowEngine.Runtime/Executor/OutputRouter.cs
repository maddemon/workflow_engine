using System.Linq;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.WaitingArea;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 节点输出路由：从 <see cref="WorkflowSchedulerKernel"/> 抽离的单一职责协作者。
/// 依据连接图将节点成功输出路由至下游节点：单输入端口目标直接入队，
/// 多输入端口目标经等待区聚合，集齐后入队。
/// </summary>
public sealed class OutputRouter
{
    private readonly INodeRegistry _nodeRegistry;
    private readonly ILogger _logger;

    /// <summary>
    /// 构造输出路由器。
    /// </summary>
    /// <param name="nodeRegistry">节点注册中心（解析目标节点类型）。</param>
    /// <param name="logger">日志。</param>
    public OutputRouter(INodeRegistry nodeRegistry, ILogger logger)
    {
        _nodeRegistry = nodeRegistry;
        _logger = logger;
    }

    /// <summary>
    /// 路由节点输出到下游：按连接图解析源端口与目标端口，单端口目标直接入队，
    /// 多端口目标经等待区聚合后入队。回边连接标记反馈激活以驱动节点上下文复用。
    /// </summary>
    /// <param name="node">源节点定义。</param>
    /// <param name="nodeType">源节点类型实例。</param>
    /// <param name="result">源节点执行结果。</param>
    /// <param name="session">执行会话（队列 / 等待区）。</param>
    /// <param name="sideEffects">副作用回调（当前仅占位，路由本身不触发副作用）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RouteOutputsAsync(
        NodeDefinition node,
        INodeType nodeType,
        NodeExecutionResult result,
        ExecutionSession session,
        IExecutionSideEffects sideEffects,
        CancellationToken cancellationToken)
    {
        var sourcePortName = ResolveSourcePortName(nodeType, result);
        var sourceKey = (node.Id, sourcePortName.ToLowerInvariant());
        var connections = session.ConnectionsBySource.Contains(sourceKey)
            ? session.ConnectionsBySource[sourceKey]
            : Enumerable.Empty<Connection>();
        var connectionList = connections.ToList();

        foreach (var connection in connectionList)
        {
            if (!session.NodeMap.TryGetValue(connection.TargetNodeId, out var targetNode))
            {
                _logger.LogWarning(
                    "RouteOutputsAsync: 目标节点 {TargetNodeId} 不存在，跳过连接 {ConnectionId}。",
                    connection.TargetNodeId,
                    connection.Id);
                continue;
            }

            var targetNodeType = _nodeRegistry.Get(targetNode.TypeName);
            var targetInputPorts = GetInputPortNames(targetNodeType);
            var outputBatch = result.Output;

            // 当 TargetPortName 为 null 时，解析为目标节点的第一个输入端口名。
            var resolvedTargetPort = connection.TargetPortName;
            if (string.IsNullOrEmpty(resolvedTargetPort) && targetInputPorts.Count > 0)
            {
                resolvedTargetPort = targetInputPorts[0];
            }

            // 标记该次激活是否来自环路回边（用于节点上下文重置判定，见 Task 9）。
            var isFeedback = session.FeedbackEdgeKeys.Contains(
                (connection.SourceNodeId, connection.SourcePortName, connection.TargetNodeId, connection.TargetPortName));

            if (targetInputPorts.Count <= 1)
            {
                var inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
                {
                    [resolvedTargetPort ?? FlowConstants.PortNames.Input] = outputBatch
                };

                await session.Queue.EnqueueAsync(
                    new NodeWorkItem(session.Execution.Id, targetNode.Id, inputs, IsFeedbackActivation: isFeedback),
                    cancellationToken).ConfigureAwait(false);
                session.PulseScheduler();
            }
            else
            {
                session.WaitingArea.Receive(session.Execution.Id, targetNode.Id, resolvedTargetPort ?? FlowConstants.PortNames.Input, outputBatch);

                if (session.WaitingArea.IsReady(session.Execution.Id, targetNode.Id, targetInputPorts))
                {
                    if (session.WaitingArea.TryTake(session.Execution.Id, targetNode.Id, out var readyInputs))
                    {
                        await session.Queue.EnqueueAsync(
                            new NodeWorkItem(session.Execution.Id, targetNode.Id, readyInputs, IsFeedbackActivation: isFeedback),
                            cancellationToken).ConfigureAwait(false);
                        session.PulseScheduler();
                    }
                }
            }
        }
    }

    /// <summary>
    /// 解析源端口名：有分支索引时取对应输出端口名，否则回退默认输出端口。
    /// </summary>
    /// <param name="nodeType">节点类型实例。</param>
    /// <param name="result">节点执行结果（含分支索引）。</param>
    /// <returns>源端口名。</returns>
    internal static string ResolveSourcePortName(INodeType nodeType, NodeExecutionResult result)
    {
        if (result.BranchIndex.HasValue)
        {
            var outputPorts = GetOutputPortNames(nodeType);
            var index = result.BranchIndex.Value;
            if (index >= 0 && index < outputPorts.Count)
            {
                return outputPorts[index];
            }
        }

        return FlowConstants.PortNames.Output;
    }

    /// <summary>
    /// 读取节点类型的全部输入端口名（按当前节点参数水合后的实例读取，避免按 TypeName 缓存串扰）。
    /// </summary>
    /// <param name="nodeType">节点类型实例。</param>
    /// <returns>输入端口名列表。</returns>
    internal static IReadOnlyList<string> GetInputPortNames(INodeType nodeType)
    {
        return nodeType.Ports
            .Where(p => p.Direction == PortDirection.Input)
            .Select(p => p.Name)
            .ToList();
    }

    /// <summary>
    /// 读取节点类型的全部输出端口名（按当前节点参数水合后的实例读取，避免按 TypeName 缓存串扰）。
    /// </summary>
    /// <param name="nodeType">节点类型实例。</param>
    /// <returns>输出端口名列表。</returns>
    internal static IReadOnlyList<string> GetOutputPortNames(INodeType nodeType)
    {
        return nodeType.Ports
            .Where(p => p.Direction == PortDirection.Output)
            .Select(p => p.Name)
            .ToList();
    }
}
