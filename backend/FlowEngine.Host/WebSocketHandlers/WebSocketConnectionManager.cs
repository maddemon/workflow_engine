using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace FlowEngine.Host.WebSocketHandlers;

/// <summary>
/// WebSocket 连接管理器，维护 executionId → 连接集合的映射。
/// </summary>
public sealed class WebSocketConnectionManager
{
    private readonly ConcurrentDictionary<Guid, HashSet<WebSocketConnection>> _subscriptions = new();
    private readonly ConcurrentDictionary<WebSocketConnection, HashSet<Guid>> _connectionSubscriptions = new();

    /// <summary>
    /// 订阅指定执行 ID 的 WebSocket 连接。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="connection">WebSocket 连接。</param>
    public void Subscribe(Guid executionId, WebSocketConnection connection)
    {
        _subscriptions.AddOrUpdate(
            executionId,
            _ =>
            {
                var set = new HashSet<WebSocketConnection> { connection };
                return set;
            },
            (_, set) =>
            {
                lock (set)
                {
                    set.Add(connection);
                }
                return set;
            });

        _connectionSubscriptions.AddOrUpdate(
            connection,
            _ => new HashSet<Guid> { executionId },
            (_, set) =>
            {
                lock (set)
                {
                    set.Add(executionId);
                }
                return set;
            });
    }

    /// <summary>
    /// 取消订阅指定执行 ID 的 WebSocket 连接。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="connection">WebSocket 连接。</param>
    public void Unsubscribe(Guid executionId, WebSocketConnection connection)
    {
        if (_subscriptions.TryGetValue(executionId, out var set))
        {
            lock (set)
            {
                set.Remove(connection);
                if (set.Count == 0)
                {
                    _subscriptions.TryRemove(executionId, out _);
                }
            }
        }

        if (_connectionSubscriptions.TryGetValue(connection, out var execIds))
        {
            lock (execIds)
            {
                execIds.Remove(executionId);
                if (execIds.Count == 0)
                {
                    _connectionSubscriptions.TryRemove(connection, out _);
                }
            }
        }
    }

    /// <summary>
    /// 获取指定执行 ID 的所有活跃连接。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <returns>连接集合的快照。</returns>
    public IReadOnlyCollection<WebSocketConnection> GetConnections(Guid executionId)
    {
        if (_subscriptions.TryGetValue(executionId, out var set))
        {
            lock (set)
            {
                return set.ToList().AsReadOnly();
            }
        }

        return Array.Empty<WebSocketConnection>();
    }

    /// <summary>
    /// 获取连接订阅的所有执行 ID。
    /// </summary>
    /// <param name="connection">WebSocket 连接。</param>
    /// <returns>执行 ID 集合的快照。</returns>
    public IReadOnlyCollection<Guid> GetSubscriptions(WebSocketConnection connection)
    {
        if (_connectionSubscriptions.TryGetValue(connection, out var set))
        {
            lock (set)
            {
                return set.ToList().AsReadOnly();
            }
        }

        return Array.Empty<Guid>();
    }

    /// <summary>
    /// 清理连接的所有订阅关系（连接关闭时调用）。
    /// </summary>
    /// <param name="connection">WebSocket 连接。</param>
    public void RemoveConnection(WebSocketConnection connection)
    {
        if (_connectionSubscriptions.TryRemove(connection, out var execIds))
        {
            lock (execIds)
            {
                foreach (var executionId in execIds)
                {
                    if (_subscriptions.TryGetValue(executionId, out var set))
                    {
                        lock (set)
                        {
                            set.Remove(connection);
                            if (set.Count == 0)
                            {
                                _subscriptions.TryRemove(executionId, out _);
                            }
                        }
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
            WebSocket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Connection closed",
                CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        WebSocket.Dispose();
    }
}
