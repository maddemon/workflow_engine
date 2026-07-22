using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Events;

/// <summary>
/// 工作流执行失败事件。
/// </summary>
public record WorkflowFailedEvent : AuditEvent
{
    /// <summary>
    /// 执行 ID。
    /// </summary>
    public Guid ExecutionId { get; init; }

    /// <summary>
    /// 工作流定义 ID。
    /// </summary>
    public Guid WorkflowDefinitionId { get; init; }

    /// <summary>
    /// 错误信息（失败节点的真实错误；失败路径未捕获到节点错误时为 null）。
    /// </summary>
    public NodeError? Error { get; init; }

    /// <summary>
    /// 初始化工作流执行失败事件。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="workflowDefinitionId">工作流定义 ID。</param>
    /// <param name="error">错误信息（失败节点的真实错误，可为 null）。</param>
    public WorkflowFailedEvent(
        Guid executionId,
        Guid workflowDefinitionId,
        NodeError? error)
    {
        ExecutionId = executionId;
        WorkflowDefinitionId = workflowDefinitionId;
        Error = error;
        EventType = AuditEventTypes.ExecutionFailed;
        ResourceType = "Execution";
        ResourceId = executionId;
    }
}
