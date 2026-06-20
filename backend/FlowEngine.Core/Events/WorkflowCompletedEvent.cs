using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Events;

/// <summary>
/// 工作流执行完成事件。
/// </summary>
public record WorkflowCompletedEvent : AuditEvent
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
    /// 最终状态。
    /// </summary>
    public ExecutionStatus FinalStatus { get; init; }

    /// <summary>
    /// 初始化工作流执行完成事件。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="workflowDefinitionId">工作流定义 ID。</param>
    /// <param name="finalStatus">最终状态。</param>
    public WorkflowCompletedEvent(
        Guid executionId,
        Guid workflowDefinitionId,
        ExecutionStatus finalStatus)
    {
        ExecutionId = executionId;
        WorkflowDefinitionId = workflowDefinitionId;
        FinalStatus = finalStatus;
        EventType = AuditEventTypes.ExecutionCompleted;
        ResourceType = "Execution";
        ResourceId = executionId;
    }
}
