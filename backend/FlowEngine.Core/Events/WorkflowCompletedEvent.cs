using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Events;

/// <summary>
/// 工作流执行完成事件。
/// </summary>
/// <param name="EventId">事件 ID。</param>
/// <param name="OccurredAt">事件发生时间。</param>
/// <param name="ExecutionId">执行 ID。</param>
/// <param name="WorkflowDefinitionId">工作流定义 ID。</param>
/// <param name="FinalStatus">最终状态。</param>
public record WorkflowCompletedEvent(
    Guid EventId,
    DateTime OccurredAt,
    Guid ExecutionId,
    Guid WorkflowDefinitionId,
    ExecutionStatus FinalStatus)
    : IDomainEvent
{
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
        : this(
            Guid.NewGuid(),
            DateTime.UtcNow,
            executionId,
            workflowDefinitionId,
            finalStatus)
    {
    }
}
