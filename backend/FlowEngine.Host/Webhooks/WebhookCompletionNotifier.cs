using FlowEngine.Core.Events;
using MediatR;

namespace FlowEngine.Host.Webhooks;

/// <summary>
/// 订阅 <see cref="WorkflowCompletedEvent"/>，将执行完成事件转发给
/// <see cref="IWebhookSyncCompletionService"/>，唤醒正在等待的同步 Webhook 请求（EX-4）。
/// 以单例形式注册为 MediatR 通知处理器，与既有 <see cref="WebSocketEventPushService"/> 等处理器并存。
/// </summary>
public sealed class WebhookCompletionNotifier : INotificationHandler<WorkflowCompletedEvent>
{
    private readonly IWebhookSyncCompletionService _completion;

    public WebhookCompletionNotifier(IWebhookSyncCompletionService completion)
    {
        _completion = completion ?? throw new ArgumentNullException(nameof(completion));
    }

    /// <inheritdoc />
    public Task Handle(WorkflowCompletedEvent notification, CancellationToken cancellationToken)
    {
        _completion.Complete(notification.ExecutionId, notification.FinalStatus);
        return Task.CompletedTask;
    }
}
