using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Events;

/// <summary>
/// 工作流执行取消事件。
/// </summary>
public record WorkflowCancelledEvent : AuditEvent
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
    /// 初始化工作流执行取消事件。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="workflowDefinitionId">工作流定义 ID。</param>
    public WorkflowCancelledEvent(
        Guid executionId,
        Guid workflowDefinitionId)
    {
        ExecutionId = executionId;
        WorkflowDefinitionId = workflowDefinitionId;
        EventType = AuditEventTypes.ExecutionCancelled;
        ResourceType = "Execution";
        ResourceId = executionId;
    }
}
