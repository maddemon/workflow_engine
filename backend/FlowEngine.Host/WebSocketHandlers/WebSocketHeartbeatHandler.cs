using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace FlowEngine.Host.WebSocketHandlers;

/// <summary>
/// WebSocket 心跳处理，负责 ping/pong 与连接保活。
/// </summary>
internal sealed class WebSocketHeartbeatHandler
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ILogger _logger;

    /// <summary>
    /// 初始化心跳处理器。
    /// </summary>
    public WebSocketHeartbeatHandler(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 运行心跳循环，定期发送 ping 并检测超时。
    /// </summary>
    public async Task RunHeartbeatAsync(WebSocketConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   connection.WebSocket.State == WebSocketState.Open)
            {
                await Task.Delay(HeartbeatInterval, cancellationToken).ConfigureAwait(false);

                var elapsed = DateTime.UtcNow - connection.LastActivityAt;
                if (elapsed > HeartbeatTimeout)
                {
                    _logger.LogWarning(
                        "Heartbeat timeout for connection {ConnectionId}, closing",
                        connection.ConnectionId);
                    break;
                }

                var ping = new WebSocketPushMessage
                {
                    Type = "ping",
                    Timestamp = DateTime.UtcNow,
                };
                await SendMessageAsync(connection, ping, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// 回复 pong 消息。
    /// </summary>
    public async Task SendPongAsync(WebSocketConnection connection, CancellationToken cancellationToken)
    {
        var pong = new WebSocketPushMessage
        {
            Type = "pong",
            Timestamp = DateTime.UtcNow,
        };
        await SendMessageAsync(connection, pong, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 发送 WebSocket 推送消息。
    /// </summary>
    internal static async Task SendMessageAsync(
        WebSocketConnection connection,
        WebSocketPushMessage message,
        CancellationToken cancellationToken)
    {
        if (connection.WebSocket.State != WebSocketState.Open)
        {
            return;
        }

        var json = JsonSerializer.Serialize(message, JsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(bytes);

        try
        {
            await connection.WebSocket.SendAsync(
                segment,
                WebSocketMessageType.Text,
                true,
                cancellationToken).ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            // Connection already closed, will be cleaned up by the main loop
        }
    }
}
