using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Events;

/// <summary>
/// 节点执行错误事件。
/// </summary>
public record NodeErrorEvent : AuditEvent
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
    /// 错误信息。
    /// </summary>
    public NodeError Error { get; init; }

    /// <summary>
    /// 初始化节点执行错误事件。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="nodeDefinitionId">节点定义 ID。</param>
    /// <param name="runIndex">运行索引。</param>
    /// <param name="error">错误信息。</param>
    public NodeErrorEvent(
        Guid executionId,
        Guid nodeDefinitionId,
        int runIndex,
        NodeError error)
    {
        ExecutionId = executionId;
        NodeDefinitionId = nodeDefinitionId;
        RunIndex = runIndex;
        Error = error;
        EventType = AuditEventTypes.NodeError;
        ResourceType = "Node";
        ResourceId = executionId;
    }
}
