using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FlowEngine.Application.Identity;
using Microsoft.AspNetCore.Http;

namespace FlowEngine.Host.WebSocketHandlers;

/// <summary>
/// 执行进度 WebSocket 端点处理器。
/// </summary>
public sealed class ExecutionWebSocketHandler
{
    private readonly WebSocketConnectionManager _connectionManager;
    private readonly WebSocketReplayService _replayService;
    private readonly IUserContext _userContext;
    private readonly ILogger<ExecutionWebSocketHandler> _logger;

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 初始化执行 WebSocket 端点处理器。
    /// </summary>
    public ExecutionWebSocketHandler(
        WebSocketConnectionManager connectionManager,
        WebSocketReplayService replayService,
        IUserContext userContext,
        ILogger<ExecutionWebSocketHandler> logger)
    {
        _connectionManager = connectionManager;
        _replayService = replayService;
        _userContext = userContext;
        _logger = logger;
    }

    /// <summary>
    /// 处理 WebSocket 握手请求。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="next">下一个中间件。</param>
    public async Task HandleAsync(HttpContext context, Func<Task> next)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            await next();
            return;
        }

        if (!_userContext.IsAuthenticated)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var webSocket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        var connection = new WebSocketConnection(webSocket)
        {
            UserId = _userContext.UserId,
        };

        _logger.LogInformation(
            "WebSocket connection established: {ConnectionId}, User: {UserId}",
            connection.ConnectionId, connection.UserId);

        try
        {
            await ProcessConnectionAsync(connection, context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "WebSocket connection error: {ConnectionId}",
                connection.ConnectionId);
        }
        finally
        {
            _connectionManager.RemoveConnection(connection);
            connection.Dispose();

            _logger.LogInformation(
                "WebSocket connection closed: {ConnectionId}",
                connection.ConnectionId);
        }
    }

    private async Task ProcessConnectionAsync(WebSocketConnection connection, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatCts = new CancellationTokenSource();
        cts.Token.Register(() => heartbeatCts.Cancel());

        _ = RunHeartbeatAsync(connection, heartbeatCts.Token);

        try
        {
            while (connection.WebSocket.State == WebSocketState.Open)
            {
                var result = await connection.WebSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await HandleClientMessageAsync(connection, message, cts.Token).ConfigureAwait(false);
                }

                connection.LastActivityAt = DateTime.UtcNow;
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        finally
        {
            heartbeatCts.Cancel();
            heartbeatCts.Dispose();
        }
    }

    private async Task HandleClientMessageAsync(
        WebSocketConnection connection,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
            {
                return;
            }

            var messageType = typeProp.GetString();
            switch (messageType)
            {
                case "subscribe":
                    var subscribeMsg = JsonSerializer.Deserialize<WebSocketSubscribeMessage>(message);
                    if (subscribeMsg is { ExecutionId: var executionId })
                    {
                        _connectionManager.Subscribe(executionId, connection);
                        _logger.LogInformation(
                            "Connection {ConnectionId} subscribed to execution {ExecutionId}",
                            connection.ConnectionId, executionId);

                        if (subscribeMsg.LastSequence.HasValue)
                        {
                            await SendMissingEventsAsync(connection, executionId, subscribeMsg.LastSequence.Value, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                    break;

                case "unsubscribe":
                    if (root.TryGetProperty("executionId", out var unsubExecId))
                    {
                        var execId = unsubExecId.GetGuid();
                        _connectionManager.Unsubscribe(execId, connection);
                        _logger.LogInformation(
                            "Connection {ConnectionId} unsubscribed from execution {ExecutionId}",
                            connection.ConnectionId, execId);
                    }
                    break;

                case "ping":
                    await SendPongAsync(connection, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Invalid JSON message from connection {ConnectionId}",
                connection.ConnectionId);
        }
    }

    private async Task SendPongAsync(WebSocketConnection connection, CancellationToken cancellationToken)
    {
        var pong = new WebSocketPushMessage
        {
            Type = "pong",
            Timestamp = DateTime.UtcNow,
        };
        await SendMessageAsync(connection, pong, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendMissingEventsAsync(
        WebSocketConnection connection,
        Guid executionId,
        long lastSequence,
        CancellationToken cancellationToken)
    {
        var missingEvents = _replayService.GetMissingEvents(executionId, lastSequence);
        if (missingEvents.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Replaying {Count} events for execution {ExecutionId} to connection {ConnectionId}",
            missingEvents.Count, executionId, connection.ConnectionId);

        foreach (var evt in missingEvents)
        {
            if (connection.WebSocket.State != System.Net.WebSockets.WebSocketState.Open)
            {
                break;
            }

            var json = JsonSerializer.Serialize(evt, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            await connection.WebSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                System.Net.WebSockets.WebSocketMessageType.Text,
                true,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunHeartbeatAsync(WebSocketConnection connection, CancellationToken cancellationToken)
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

    internal async Task SendMessageAsync(
        WebSocketConnection connection,
        WebSocketPushMessage message,
        CancellationToken cancellationToken)
    {
        if (connection.WebSocket.State != WebSocketState.Open)
        {
            return;
        }

        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
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
