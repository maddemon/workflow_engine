namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 领域事件基接口。
/// </summary>
public interface IDomainEvent
{
    /// <summary>
    /// 事件 ID。
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// 事件发生时间。
    /// </summary>
    DateTime OccurredAt { get; }
}
