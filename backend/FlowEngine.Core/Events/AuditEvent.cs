using FlowEngine.Core.Abstractions;

namespace FlowEngine.Core.Events;

/// <summary>
/// 审计事件基类。
/// </summary>
public abstract record AuditEvent : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 事件类型（如 "Workflow.Created"）。
    /// </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>
    /// 操作人/触发源。
    /// </summary>
    public string Actor { get; init; } = string.Empty;

    /// <summary>
    /// 资源类型（Workflow、Execution、User、Credential 等）。
    /// </summary>
    public string ResourceType { get; init; } = string.Empty;

    /// <summary>
    /// 资源 ID。
    /// </summary>
    public Guid ResourceId { get; init; }

    /// <summary>
    /// 事件体，包含具体上下文。
    /// </summary>
    public Dictionary<string, object>? Payload { get; init; }

    /// <summary>
    /// 客户端 IP、UserAgent 等元数据。
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}
