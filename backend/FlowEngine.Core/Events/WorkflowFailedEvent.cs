using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Events;

/// <summary>
/// 工作流执行失败事件。
/// </summary>
/// <param name="EventId">事件 ID。</param>
/// <param name="OccurredAt">事件发生时间。</param>
/// <param name="ExecutionId">执行 ID。</param>
/// <param name="WorkflowDefinitionId">工作流定义 ID。</param>
/// <param name="Error">错误信息。</param>
public record WorkflowFailedEvent(
    Guid EventId,
    DateTime OccurredAt,
    Guid ExecutionId,
    Guid WorkflowDefinitionId,
    NodeError Error)
    : IDomainEvent
{
    /// <summary>
    /// 初始化工作流执行失败事件。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="workflowDefinitionId">工作流定义 ID。</param>
    /// <param name="error">错误信息。</param>
    public WorkflowFailedEvent(
        Guid executionId,
        Guid workflowDefinitionId,
        NodeError error)
        : this(
            Guid.NewGuid(),
            DateTime.UtcNow,
            executionId,
            workflowDefinitionId,
            error)
    {
    }
}
