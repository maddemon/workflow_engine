namespace FlowEngine.Core.Events;

/// <summary>
/// 节点开始执行事件。
/// </summary>
public record NodeStartedEvent : AuditEvent
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
    /// 初始化节点开始执行事件。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="nodeDefinitionId">节点定义 ID。</param>
    /// <param name="runIndex">运行索引。</param>
    public NodeStartedEvent(
        Guid executionId,
        Guid nodeDefinitionId,
        int runIndex)
    {
        ExecutionId = executionId;
        NodeDefinitionId = nodeDefinitionId;
        RunIndex = runIndex;
        EventType = AuditEventTypes.NodeStarted;
        ResourceType = "Node";
        ResourceId = executionId;
    }
}
