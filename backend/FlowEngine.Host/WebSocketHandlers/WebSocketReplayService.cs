namespace FlowEngine.Host.WebSocketHandlers;

/// <summary>
/// 事件重放服务，用于断线重连时补发缺失事件。
/// 不直接订阅 EventBus —— 由 WebSocketEventPushService 将已赋序号的事件推送过来存储。
/// </summary>
public sealed class WebSocketReplayService
{
    private readonly ILogger<WebSocketReplayService> _logger;
    private readonly Dictionary<Guid, List<WebSocketPushMessage>> _eventHistory = new();
    private readonly object _lock = new();

    private const int MaxEventsPerExecution = 1000;

    public WebSocketReplayService(ILogger<WebSocketReplayService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 记录一条已赋序号的事件，由推送服务调用。
    /// </summary>
    public void RecordEvent(Guid executionId, WebSocketPushMessage message)
    {
        lock (_lock)
        {
            if (!_eventHistory.TryGetValue(executionId, out var events))
            {
                events = new List<WebSocketPushMessage>();
                _eventHistory[executionId] = events;
            }

            events.Add(message);

            if (events.Count > MaxEventsPerExecution)
            {
                events.RemoveRange(0, events.Count - MaxEventsPerExecution);
            }
        }

        _logger.LogDebug(
            "Recorded event {Type} for execution {ExecutionId}, sequence {Sequence}",
            message.Type, executionId, message.Sequence);
    }

    /// <summary>
    /// 获取指定执行 ID 的缺失事件（从 lastSequence 之后的事件）。
    /// </summary>
    public IReadOnlyList<WebSocketPushMessage> GetMissingEvents(Guid executionId, long lastSequence)
    {
        lock (_lock)
        {
            if (!_eventHistory.TryGetValue(executionId, out var events))
            {
                return Array.Empty<WebSocketPushMessage>();
            }

            return events.Where(e => e.Sequence > lastSequence).ToList().AsReadOnly();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            _eventHistory.Clear();
        }
    }
}
