using FlowEngine.Core.Abstractions;

namespace FlowEngine.Core.Events;

/// <summary>
/// 工作流启动事件。
/// </summary>
/// <param name="EventId">事件 ID。</param>
/// <param name="OccurredAt">事件发生时间。</param>
/// <param name="ExecutionId">执行 ID。</param>
/// <param name="WorkflowDefinitionId">工作流定义 ID。</param>
/// <param name="TriggerPayload">触发负载。</param>
public record WorkflowStartedEvent(
    Guid EventId,
    DateTime OccurredAt,
    Guid ExecutionId,
    Guid WorkflowDefinitionId,
    object? TriggerPayload)
    : IDomainEvent
{
    /// <summary>
    /// 初始化工作流启动事件。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="workflowDefinitionId">工作流定义 ID。</param>
    /// <param name="triggerPayload">触发负载。</param>
    public WorkflowStartedEvent(
        Guid executionId,
        Guid workflowDefinitionId,
        object? triggerPayload = null)
        : this(
            Guid.NewGuid(),
            DateTime.UtcNow,
            executionId,
            workflowDefinitionId,
            triggerPayload)
    {
    }
}
