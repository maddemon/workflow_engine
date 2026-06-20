using FlowEngine.Core.Abstractions;

namespace FlowEngine.Core.Events;

/// <summary>
/// 工作流启动事件。
/// </summary>
public record WorkflowStartedEvent : AuditEvent
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
    /// 触发负载。
    /// </summary>
    public object? TriggerPayload { get; init; }

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
    {
        ExecutionId = executionId;
        WorkflowDefinitionId = workflowDefinitionId;
        TriggerPayload = triggerPayload;
        EventType = AuditEventTypes.ExecutionStarted;
        ResourceType = "Execution";
        ResourceId = executionId;
    }
}
