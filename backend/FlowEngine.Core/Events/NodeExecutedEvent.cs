using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Events;

/// <summary>
/// 节点执行完成事件。
/// </summary>
/// <param name="EventId">事件 ID。</param>
/// <param name="OccurredAt">事件发生时间。</param>
/// <param name="ExecutionId">执行 ID。</param>
/// <param name="NodeDefinitionId">节点定义 ID。</param>
/// <param name="RunIndex">运行索引。</param>
/// <param name="Result">节点执行结果。</param>
public record NodeExecutedEvent(
    Guid EventId,
    DateTime OccurredAt,
    Guid ExecutionId,
    Guid NodeDefinitionId,
    int RunIndex,
    NodeExecutionResult Result)
    : IDomainEvent
{
    /// <summary>
    /// 初始化节点执行完成事件。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="nodeDefinitionId">节点定义 ID。</param>
    /// <param name="runIndex">运行索引。</param>
    /// <param name="result">节点执行结果。</param>
    public NodeExecutedEvent(
        Guid executionId,
        Guid nodeDefinitionId,
        int runIndex,
        NodeExecutionResult result)
        : this(
            Guid.NewGuid(),
            DateTime.UtcNow,
            executionId,
            nodeDefinitionId,
            runIndex,
            result)
    {
    }
}
