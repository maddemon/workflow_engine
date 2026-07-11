using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Agent;

/// <summary>
/// 工具执行记录器，构造节点执行记录。
/// </summary>
/// <remarks>
/// 注意：当前构造的 <see cref="NodeExecutionRecord"/> 未写入任何存储或日志，
/// 保留此行为以兼容原有逻辑，后续可通过 parentContext.Logger 或回调发布。
/// </remarks>
internal sealed class ToolExecutionRecorder
{
    /// <summary>
    /// 构造节点执行记录。
    /// </summary>
    public NodeExecutionRecord Record(
        NodeDefinition toolNode,
        NodeExecutionContext toolContext,
        NodeExecutionResult result,
        DateTime startedAt,
        Guid? parentRecordId)
    {
        return new NodeExecutionRecord
        {
            Id = Guid.NewGuid(),
            NodeDefinitionId = toolNode.Id,
            RunIndex = 0,
            StartedAt = startedAt,
            CompletedAt = DateTime.UtcNow,
            Inputs = toolContext.Inputs.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            Output = result,
            RawParameters = toolContext.RawParameters.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            ResolvedParameters = toolContext.ResolvedParameters.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            ParentRecordId = parentRecordId
        };
    }
}
