using System.Collections.Concurrent;
using System.Collections.Generic;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Security;

namespace FlowEngine.Runtime.Execution.Stages;

/// <summary>
/// 节点执行相关的纯函数助手：运行输入构建、保留输出限流、执行记录脱敏构造、LLM 客户端解析。
/// 逻辑与历史 <see cref="NodeProcessor"/> 实现保持一致，供 <see cref="ExecutionStage"/> 与
/// 遗留反射测试（经 <see cref="NodeProcessor"/> 包装方法）共用，确保单一事实来源。
/// </summary>
internal static class NodeExecutionHelpers
{
    /// <summary>
    /// 解析节点运行输入：非 OncePerItem 原样透传；OncePerItem 按 runIndex 取各端口第 runIndex 个数据项。
    /// </summary>
    /// <param name="inputs">原始按端口组织的输入批。</param>
    /// <param name="mode">节点执行模式。</param>
    /// <param name="runIndex">当前运行索引。</param>
    /// <returns>本次运行的输入。</returns>
    public static IReadOnlyDictionary<string, DataBatch> BuildRunInputs(
        IReadOnlyDictionary<string, DataBatch> inputs,
        ExecutionMode mode,
        int runIndex)
    {
        if (mode != ExecutionMode.OncePerItem)
        {
            return inputs;
        }

        var result = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase);
        foreach (var (portName, batch) in inputs)
        {
            if (runIndex < batch.Items.Count)
            {
                result[portName] = new DataBatch
                {
                    Items = [batch.Items[runIndex]]
                };
            }
            else
            {
                result[portName] = new DataBatch();
            }
        }

        return result;
    }

    /// <summary>
    /// 限制单节点保留输出项数（CON-5）：超过上限时仅保留最新 N 项，作用于
    /// <see cref="ExecutionSession.SuccessfulOutputs"/> 与 <see cref="ExecutionSession.LatestBatches"/>。
    /// </summary>
    /// <param name="session">执行会话。</param>
    /// <param name="nodeName">节点名。</param>
    /// <param name="maxRetainedOutputItems">保留项数上限（&gt;0）。</param>
    public static void CapRetainedOutput(ExecutionSession session, string nodeName, int maxRetainedOutputItems)
    {
        var max = maxRetainedOutputItems;
        if (session.SuccessfulOutputs.TryGetValue(nodeName, out var so) && so.Items.Count > max)
        {
            session.SuccessfulOutputs[nodeName] = Cap(so, max);
        }

        if (session.LatestBatches.TryGetValue(nodeName, out var lb) && lb.Items.Count > max)
        {
            session.LatestBatches[nodeName] = Cap(lb, max);
        }
    }

    /// <summary>
    /// 截断为最新 max 项（OncePerItem 按 SourceIndex 升序累积，末段即最近输出）。
    /// </summary>
    /// <param name="batch">原始批。</param>
    /// <param name="max">保留项数上限。</param>
    /// <returns>截断后的批。</returns>
    public static DataBatch Cap(DataBatch batch, int max)
    {
        // 保留最新 max 项（OncePerItem 按 SourceIndex 升序累积，末段即最近输出）。
        return new DataBatch { Items = batch.Items.Skip(batch.Items.Count - max).ToList() };
    }

    /// <summary>
    /// 由节点执行上下文构造节点执行记录，并对输入/输出/参数做敏感值脱敏。
    /// </summary>
    /// <param name="nodeDefinitionId">节点定义 ID。</param>
    /// <param name="runIndex">运行索引。</param>
    /// <param name="inputs">本次运行输入。</param>
    /// <param name="output">本次运行输出。</param>
    /// <param name="context">节点执行上下文（含脱敏所需的原始/已解析参数与记录 ID）。</param>
    /// <param name="sensitiveValues">敏感值集合（字面凭据）。</param>
    /// <param name="startedAt">节点执行开始时间。</param>
    /// <param name="secretMasker">敏感值脱敏器。</param>
    /// <returns>脱敏后的节点执行记录。</returns>
    public static NodeExecutionRecord BuildNodeExecutionRecord(
        string nodeDefinitionId,
        int runIndex,
        IReadOnlyDictionary<string, DataBatch> inputs,
        NodeExecutionResult output,
        NodeExecutionContext context,
        IReadOnlySet<string> sensitiveValues,
        DateTime startedAt,
        SecretMasker secretMasker)
    {
        return new NodeExecutionRecord
        {
            Id = context.NodeExecutionRecordId,
            NodeDefinitionId = nodeDefinitionId,
            RunIndex = runIndex,
            StartedAt = startedAt,
            CompletedAt = DateTime.UtcNow,
            Inputs = inputs.ToDictionary(kv => kv.Key, kv => secretMasker.MaskDataBatch(kv.Value, sensitiveValues), StringComparer.OrdinalIgnoreCase),
            Output = secretMasker.MaskOutput(output, sensitiveValues),
            RawParameters = secretMasker.MaskParameters(context.RawParameters, sensitiveValues),
            ResolvedParameters = secretMasker.MaskParameters(context.ResolvedParameters, sensitiveValues)
        };
    }

    /// <summary>
    /// 为 LLM 类节点解析其上游供给的 LLM 客户端：遍历节点的 LLM 输入端口，
    /// 沿入边在 <see cref="ExecutionSession.NodeLlmClients"/> 中查找上游已注册的客户端。
    /// </summary>
    /// <param name="node">当前节点定义。</param>
    /// <param name="nodeType">当前节点类型实例。</param>
    /// <param name="nodeMap">节点映射。</param>
    /// <param name="connectionsBySource">按源端口分组的连接查找。</param>
    /// <param name="nodeLlmClients">节点 LLM 客户端注册表。</param>
    /// <returns>解析到的 LLM 客户端；无则返回 null。</returns>
    public static ILlmClient? ResolveLlmClientForNode(
        NodeDefinition node,
        INodeType nodeType,
        Dictionary<string, NodeDefinition> nodeMap,
        ILookup<(string SourceNodeId, string SourcePortName), Connection> connectionsBySource,
        ConcurrentDictionary<string, ILlmClient> nodeLlmClients)
    {
        var supplyInputPorts = nodeType.Ports
            .Where(p => p.Direction == PortDirection.Input && p.Type == PortType.LLM)
            .ToList();

        if (supplyInputPorts.Count == 0)
        {
            return null;
        }

        foreach (var port in supplyInputPorts)
        {
            var incomingConnections = connectionsBySource
                .Where(g => g.Key.SourceNodeId != node.Id)
                .SelectMany(g => g)
                .Where(c => c.TargetNodeId == node.Id && c.TargetPortName is not null && c.TargetPortName.Equals(port.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var connection in incomingConnections)
            {
                if (nodeLlmClients.TryGetValue(connection.SourceNodeId, out var client))
                {
                    return client;
                }
            }
        }

        return null;
    }
}
