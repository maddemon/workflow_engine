using System.Threading.Channels;
using FlowEngine.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Core.Events;

/// <summary>
/// 内部错误 Sink，收集订阅者处理事件时抛出的异常。
/// </summary>
public sealed class InternalErrorSink
{
    private readonly ILogger? _logger;

    /// <summary>
    /// 错误计数。
    /// </summary>
    public long ErrorCount { get; private set; }

    /// <summary>
    /// 初始化内部错误 Sink。
    /// </summary>
    /// <param name="logger">可选日志记录器。</param>
    public InternalErrorSink(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 记录订阅者处理事件时的异常。
    /// </summary>
    /// <param name="eventType">事件类型名。</param>
    /// <param name="handlerType">处理程序类型名。</param>
    /// <param name="exception">异常。</param>
    public void Record(string eventType, string handlerType, Exception exception)
    {
        ErrorCount++;
        _logger?.LogError(exception,
            "Event handler {HandlerType} threw processing {EventType}",
            handlerType, eventType);
    }
}

/// <summary>
/// 内存事件总线实现，使用有界 Channel 做背压，后台单线程消费，订阅者异常隔离。
/// </summary>
public sealed class InMemoryEventBus : IEventBus, IDisposable
{
    private readonly Channel<IDomainEvent> _channel =
        Channel.CreateBounded<IDomainEvent>(new BoundedChannelOptions(10000)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropWrite,
        });

    private readonly Dictionary<Type, List<Func<IDomainEvent, CancellationToken, Task>>> _handlers = new();
    private readonly object _lock = new();
    private readonly InternalErrorSink _errorSink;
    private readonly ILogger<InMemoryEventBus>? _logger;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// 初始化内存事件总线。
    /// </summary>
    /// <param name="errorSink">内部错误 Sink。</param>
    /// <param name="logger">可选日志记录器。</param>
    public InMemoryEventBus(InternalErrorSink errorSink, ILogger<InMemoryEventBus>? logger = null)
    {
        _errorSink = errorSink;
        _logger = logger;
        _ = ProcessLoopAsync();
    }

    /// <inheritdoc />
    public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        if (!_channel.Writer.TryWrite(eventInstance))
        {
            _logger?.LogWarning("Event bus channel full, dropping event {EventType}", typeof(TEvent).Name);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : IDomainEvent
    {
        Func<IDomainEvent, CancellationToken, Task> wrapper =
            (e, ct) => handler((TEvent)e, ct);

        lock (_lock)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var list))
            {
                list = new List<Func<IDomainEvent, CancellationToken, Task>>();
                _handlers[typeof(TEvent)] = list;
            }

            list.Add(wrapper);
        }

        return new Subscription(() =>
        {
            lock (_lock)
            {
                if (_handlers.TryGetValue(typeof(TEvent), out var list))
                {
                    list.Remove(wrapper);
                }
            }
        });
    }

    private async Task ProcessLoopAsync()
    {
        var reader = _channel.Reader;
        try
        {
            while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var @event))
                {
                    await DispatchAsync(@event).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
    }

    private async Task DispatchAsync(IDomainEvent @event)
    {
        List<Func<IDomainEvent, CancellationToken, Task>>? handlers;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(@event.GetType(), out handlers))
            {
                return;
            }

            handlers = new List<Func<IDomainEvent, CancellationToken, Task>>(handlers);
        }

        foreach (var handler in handlers)
        {
            try
            {
                await handler(@event, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _errorSink.Record(
                    @event.GetType().Name,
                    handler.Method?.DeclaringType?.Name ?? "Unknown",
                    ex);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();
        _cts.Dispose();
    }

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
