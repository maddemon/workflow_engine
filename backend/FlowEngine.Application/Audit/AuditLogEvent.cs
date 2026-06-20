using FlowEngine.Core.Events;

namespace FlowEngine.Application.Audit;

/// <summary>
/// 通用审计日志事件，用于记录业务操作到审计日志。
/// </summary>
public sealed record AuditLogEvent : AuditEvent;
