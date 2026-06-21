using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace FlowEngine.Host.WebSocketHandlers;

/// <summary>
/// WebSocket 连接管理器，维护 executionId → 连接集合的映射。
/// </summary>
public sealed class WebSocketConnectionManager
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<WebSocketConnection, byte>> _subscriptions = new();
    private readonly ConcurrentDictionary<WebSocketConnection, ConcurrentDictionary<Guid, byte>> _connectionSubscriptions = new();

    /// <summary>
    /// 订阅指定执行 ID 的 WebSocket 连接。
    /// </summary>
    public void Subscribe(Guid executionId, WebSocketConnection connection)
    {
        var set = _subscriptions.GetOrAdd(executionId, _ => new ConcurrentDictionary<WebSocketConnection, byte>());
        set.TryAdd(connection, 0);

        var execIds = _connectionSubscriptions.GetOrAdd(connection, _ => new ConcurrentDictionary<Guid, byte>());
        execIds.TryAdd(executionId, 0);
    }

    /// <summary>
    /// 取消订阅指定执行 ID 的 WebSocket 连接。
    /// </summary>
    public void Unsubscribe(Guid executionId, WebSocketConnection connection)
    {
        if (_subscriptions.TryGetValue(executionId, out var set))
        {
            set.TryRemove(connection, out _);
            if (set.IsEmpty)
            {
                _subscriptions.TryRemove(executionId, out _);
            }
        }

        if (_connectionSubscriptions.TryGetValue(connection, out var execIds))
        {
            execIds.TryRemove(executionId, out _);
            if (execIds.IsEmpty)
            {
                _connectionSubscriptions.TryRemove(connection, out _);
            }
        }
    }

    /// <summary>
    /// 获取指定执行 ID 的所有活跃连接。
    /// </summary>
    public IReadOnlyCollection<WebSocketConnection> GetConnections(Guid executionId)
    {
        if (_subscriptions.TryGetValue(executionId, out var set))
        {
            return set.Keys.ToList().AsReadOnly();
        }

        return Array.Empty<WebSocketConnection>();
    }

    /// <summary>
    /// 获取连接订阅的所有执行 ID。
    /// </summary>
    public IReadOnlyCollection<Guid> GetSubscriptions(WebSocketConnection connection)
    {
        if (_connectionSubscriptions.TryGetValue(connection, out var set))
        {
            return set.Keys.ToList().AsReadOnly();
        }

        return Array.Empty<Guid>();
    }

    /// <summary>
    /// 清理连接的所有订阅关系（连接关闭时调用）。
    /// </summary>
    public void RemoveConnection(WebSocketConnection connection)
    {
        if (_connectionSubscriptions.TryRemove(connection, out var execIds))
        {
            foreach (var executionId in execIds.Keys)
            {
                if (_subscriptions.TryGetValue(executionId, out var set))
                {
                    set.TryRemove(connection, out _);
                    if (set.IsEmpty)
                    {
                        _subscriptions.TryRemove(executionId, out _);
                    }
                }
            }
        }
    }
}

/// <summary>
/// WebSocket 连接封装。
/// </summary>
public sealed class WebSocketConnection : IDisposable
{
    /// <summary>
    /// 连接 ID。
    /// </summary>
    public Guid ConnectionId { get; } = Guid.NewGuid();

    /// <summary>
    /// 底层 WebSocket。
    /// </summary>
    public WebSocket WebSocket { get; }

    /// <summary>
    /// 连接建立时间。
    /// </summary>
    public DateTime ConnectedAt { get; } = DateTime.UtcNow;

    /// <summary>
    /// 最后活跃时间（收到 pong 或消息时更新）。
    /// </summary>
    public DateTime LastActivityAt { get; set; }

    /// <summary>
    /// 连接关联的用户 ID。
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 初始化 WebSocket 连接。
    /// </summary>
    /// <param name="webSocket">底层 WebSocket。</param>
    public WebSocketConnection(WebSocket webSocket)
    {
        WebSocket = webSocket;
        LastActivityAt = DateTime.UtcNow;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (WebSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await WebSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Connection closed",
                        cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort close; socket will be disposed regardless.
                }
            });
        }

        WebSocket.Dispose();
    }
}
