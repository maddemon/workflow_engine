using FlowEngine.Core.Abstractions;

namespace FlowEngine.Core.Events;

/// <summary>
/// 审计事件基类。
/// </summary>
/// <param name="EventId">事件 ID。</param>
/// <param name="OccurredAt">事件发生时间。</param>
public abstract record AuditEvent(Guid EventId, DateTime OccurredAt) : IDomainEvent;
