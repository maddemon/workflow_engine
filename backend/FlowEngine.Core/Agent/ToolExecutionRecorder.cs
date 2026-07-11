using FlowEngine.Core.Entities;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Core.Agent;

/// <summary>
/// 工具执行记录器，构造节点执行记录并写入日志，避免工具级遥测丢失。
/// </summary>
internal sealed class ToolExecutionRecorder(ILogger? logger = null)
{
    /// <summary>
    /// 构造节点执行记录，并以 Debug 级别写入日志，便于排查工具执行结果。
    /// </summary>
    public NodeExecutionRecord Record(
        NodeDefinition toolNode,
        NodeExecutionContext toolContext,
        NodeExecutionResult result,
        DateTime startedAt,
        Guid? parentRecordId)
    {
        var completedAt = DateTime.UtcNow;
        var record = new NodeExecutionRecord
        {
            Id = Guid.NewGuid(),
            NodeDefinitionId = toolNode.Id,
            RunIndex = 0,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Inputs = toolContext.Inputs.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            Output = result,
            RawParameters = toolContext.RawParameters.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            ResolvedParameters = toolContext.ResolvedParameters.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            ParentRecordId = parentRecordId
        };

        logger?.LogDebug(
            "工具节点 {NodeType} 执行完成：Success={Success}, 耗时={Elapsed}ms。",
            toolNode.TypeName,
            result.Success,
            (completedAt - startedAt).TotalMilliseconds);

        return record;
    }
}
