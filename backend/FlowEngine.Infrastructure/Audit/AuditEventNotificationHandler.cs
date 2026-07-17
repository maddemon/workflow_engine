using FlowEngine.Application.Audit;
using FlowEngine.Application.ExecutionCleanup;
using FlowEngine.Core.Events;
using MediatR;

namespace FlowEngine.Infrastructure.Audit;

/// <summary>
/// 将全部 <see cref="AuditEvent"/> 子类型（领域执行事件与业务审计事件）转发至
/// <see cref="AuditLogFileSink"/> 写入 NDJSON 审计日志。
/// 替代原 <see cref="AuditLogFileSink"/> 通过 <c>IEventBus.Subscribe&lt;AuditEvent&gt;</c> 的订阅。
/// </summary>
/// <remarks>
/// MediatR 按事件的精确运行时类型分派通知处理器，因此需为每个 <see cref="AuditEvent"/> 子类型
/// 显式注册处理器（基类订阅无法被 MediatR 自动继承）。新增 <see cref="AuditEvent"/> 子类型时，
/// 必须在此补充对应接口实现，否则该事件不会被写入审计日志。
/// </remarks>
public sealed class AuditEventNotificationHandler(AuditLogFileSink sink) :
    INotificationHandler<AuditLogEvent>,
    INotificationHandler<ExecutionCleanupEvent>,
    INotificationHandler<WorkflowStartedEvent>,
    INotificationHandler<WorkflowCompletedEvent>,
    INotificationHandler<WorkflowFailedEvent>,
    INotificationHandler<WorkflowCancelledEvent>,
    INotificationHandler<NodeStartedEvent>,
    INotificationHandler<NodeExecutedEvent>,
    INotificationHandler<NodeErrorEvent>,
    INotificationHandler<CredentialAccessedEvent>
{
    /// <inheritdoc />
    public Task Handle(AuditLogEvent notification, CancellationToken cancellationToken)
        => sink.OnEventAsync(notification, cancellationToken);

    /// <inheritdoc />
    public Task Handle(ExecutionCleanupEvent notification, CancellationToken cancellationToken)
        => sink.OnEventAsync(notification, cancellationToken);

    /// <inheritdoc />
    public Task Handle(WorkflowStartedEvent notification, CancellationToken cancellationToken)
        => sink.OnEventAsync(notification, cancellationToken);

    /// <inheritdoc />
    public Task Handle(WorkflowCompletedEvent notification, CancellationToken cancellationToken)
        => sink.OnEventAsync(notification, cancellationToken);

    /// <inheritdoc />
    public Task Handle(WorkflowFailedEvent notification, CancellationToken cancellationToken)
        => sink.OnEventAsync(notification, cancellationToken);

    /// <inheritdoc />
    public Task Handle(WorkflowCancelledEvent notification, CancellationToken cancellationToken)
        => sink.OnEventAsync(notification, cancellationToken);

    /// <inheritdoc />
    public Task Handle(NodeStartedEvent notification, CancellationToken cancellationToken)
        => sink.OnEventAsync(notification, cancellationToken);

    /// <inheritdoc />
    public Task Handle(NodeExecutedEvent notification, CancellationToken cancellationToken)
        => sink.OnEventAsync(notification, cancellationToken);

    /// <inheritdoc />
    public Task Handle(NodeErrorEvent notification, CancellationToken cancellationToken)
        => sink.OnEventAsync(notification, cancellationToken);

    /// <inheritdoc />
    public Task Handle(CredentialAccessedEvent notification, CancellationToken cancellationToken)
        => sink.OnEventAsync(notification, cancellationToken);
}
