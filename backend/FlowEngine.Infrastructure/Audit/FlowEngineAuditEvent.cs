using Audit.Core;

namespace FlowEngine.Infrastructure.Audit;

/// <summary>
/// 映射到 Audit.NET 的审计事件模型。
/// 仅承载与历史 NDJSON 审计日志格式一致的字段，序列化由 <see cref="FlowEngineAuditJsonAdapter"/> 负责，
/// 以保证 <see cref="AuditLogReader"/> 与既有 API 消费者读取的字段布局不变。
/// </summary>
public sealed class FlowEngineAuditEvent : AuditEvent
{
    /// <summary>事件 ID（对应 <c>AuditEvent.EventId</c>）。</summary>
    public Guid Id { get; set; }

    /// <summary>事件发生的 UTC 时间戳（对应 <c>AuditEvent.OccurredAt</c>）。</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>操作人 / 触发源。</summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>资源类型（Workflow、Execution、Credential 等）。</summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>资源 ID。</summary>
    public Guid ResourceId { get; set; }

    /// <summary>事件体，包含具体上下文。</summary>
    public Dictionary<string, object>? Payload { get; set; }

    /// <summary>客户端 IP、UserAgent 等元数据。</summary>
    public Dictionary<string, string>? Metadata { get; set; }
}
