using FlowEngine.Core.Abstractions;
using MediatR;

namespace FlowEngine.Core.Events;

/// <summary>
/// LLM 流式 token 输出事件，由 AgentNode 等 LLM 节点在流式调用过程中逐 chunk 发布。
/// </summary>
public record LlmTokenStreamEvent : IDomainEvent, INotification
{
    /// <inheritdoc />
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 事件类型（固定 <see cref="AuditEventTypes.LlmTokenStream"/>）。
    /// </summary>
    public string EventType { get; init; } = AuditEventTypes.LlmTokenStream;

    /// <summary>
    /// 执行 ID。
    /// </summary>
    public Guid ExecutionId { get; init; }

    /// <summary>
    /// 节点定义 ID（引用 NodeDefinition.Id 的字符串标识）。
    /// </summary>
    public string NodeDefinitionId { get; init; } = string.Empty;

    /// <summary>
    /// 运行索引。
    /// </summary>
    public int RunIndex { get; init; }

    /// <summary>
    /// 增量 token 文本（最后一条可能为 null）。
    /// </summary>
    public string? Delta { get; init; }

    /// <summary>
    /// 是否为流的最后一条 chunk。
    /// </summary>
    public bool IsFinal { get; init; }
}
