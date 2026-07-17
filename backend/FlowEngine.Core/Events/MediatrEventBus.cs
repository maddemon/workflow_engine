using FlowEngine.Core.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Core.Events;

/// <summary>
/// 基于 MediatR 的事件总线实现，替代手搓的 <see cref="InMemoryEventBus"/>。
/// <list type="bullet">
///   <item><description><see cref="PublishAsync"/> 委托给 <see cref="IMediator.Publish"/>，由注册的
///     <see cref="INotificationHandler{TNotification}"/> 处理（审计落盘、WebSocket 推送等静态消费者）。</description></item>
///   <item><description><see cref="Subscribe"/> 保留为兼容垫片，供动态订阅者使用（如 SSE 控制器按连接订阅），
///     因为 MediatR 的通知分派是静态的、无法表达按连接的动态订阅。</description></item>
/// </list>
/// </summary>
public sealed class MediatrEventBus(IMediator mediator, ILogger<MediatrEventBus>? logger = null) : IEventBus
{
    private readonly Dictionary<Type, List<Func<IDomainEvent, CancellationToken, Task>>> _subscribers = new();
    private readonly object _lock = new();

    /// <inheritdoc />
    public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        // 静态消费者走 MediatR 通知处理器。
        Task mediatorTask = Task.CompletedTask;
        if (eventInstance is INotification notification)
        {
            mediatorTask = mediator.Publish(notification, cancellationToken);
        }

        // 动态订阅者（SSE）兼容分派——保留原事件总线的 Subscribe 语义。
        Task subscriberTask = DispatchToSubscribersAsync(eventInstance, cancellationToken);

        return Task.WhenAll(mediatorTask, subscriberTask);
    }

    /// <inheritdoc />
    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : IDomainEvent
    {
        Func<IDomainEvent, CancellationToken, Task> wrapper = (e, ct) => handler((TEvent)e, ct);

        lock (_lock)
        {
            if (!_subscribers.TryGetValue(typeof(TEvent), out var list))
            {
                list = new List<Func<IDomainEvent, CancellationToken, Task>>();
                _subscribers[typeof(TEvent)] = list;
            }

            list.Add(wrapper);
        }

        return new Subscription(() =>
        {
            lock (_lock)
            {
                if (_subscribers.TryGetValue(typeof(TEvent), out var list))
                {
                    list.Remove(wrapper);
                }
            }
        });
    }

    private async Task DispatchToSubscribersAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        List<Func<IDomainEvent, CancellationToken, Task>>? handlers = null;

        lock (_lock)
        {
            // 匹配精确类型及其所有基类型（含接口），与原总线按类型分发语义一致。
            foreach (var kvp in _subscribers)
            {
                if (kvp.Key.IsAssignableFrom(@event.GetType()))
                {
                    handlers ??= new List<Func<IDomainEvent, CancellationToken, Task>>();
                    handlers.AddRange(kvp.Value);
                }
            }
        }

        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers)
        {
            try
            {
                await handler(@event, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 订阅者异常隔离，避免影响其他消费者与发布者（与原总线语义一致）。
                logger?.LogError(ex, "事件订阅者处理 {EventType} 时出错", @event.GetType().Name);
            }
        }
    }

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
