using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Events;

/// <summary>
/// 节点执行完成事件。
/// </summary>
public record NodeExecutedEvent : AuditEvent
{
    /// <summary>
    /// 执行 ID。
    /// </summary>
    public Guid ExecutionId { get; init; }

    /// <summary>
    /// 节点定义 ID。
    /// </summary>
    public Guid NodeDefinitionId { get; init; }

    /// <summary>
    /// 运行索引。
    /// </summary>
    public int RunIndex { get; init; }

    /// <summary>
    /// 节点执行结果。
    /// </summary>
    public NodeExecutionResult Result { get; init; }

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
    {
        ExecutionId = executionId;
        NodeDefinitionId = nodeDefinitionId;
        RunIndex = runIndex;
        Result = result;
        EventType = AuditEventTypes.NodeExecuted;
        ResourceType = "Node";
        ResourceId = executionId;
    }
}
