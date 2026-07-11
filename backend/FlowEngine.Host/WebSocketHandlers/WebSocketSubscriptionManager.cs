using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FlowEngine.Core.Authorization;

namespace FlowEngine.Host.WebSocketHandlers;

/// <summary>
/// WebSocket 订阅管理，处理 subscribe/unsubscribe 消息与事件补发。
/// </summary>
internal sealed class WebSocketSubscriptionManager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly WebSocketConnectionManager _connectionManager;
    private readonly WebSocketReplayService _replayService;
    private readonly WebSocketAuthenticator _authenticator;
    private readonly ILogger _logger;

    /// <summary>
    /// 初始化订阅管理器。
    /// </summary>
    public WebSocketSubscriptionManager(
        WebSocketConnectionManager connectionManager,
        WebSocketReplayService replayService,
        WebSocketAuthenticator authenticator,
        ILogger logger)
    {
        _connectionManager = connectionManager;
        _replayService = replayService;
        _authenticator = authenticator;
        _logger = logger;
    }

    /// <summary>
    /// 处理客户端消息，分发 subscribe/unsubscribe/ping。
    /// </summary>
    public async Task HandleMessageAsync(
        WebSocketConnection connection,
        string message,
        WebSocketHeartbeatHandler heartbeat,
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
                        await HandleSubscribeAsync(
                            connection, executionId, subscribeMsg.LastSequence, cancellationToken)
                            .ConfigureAwait(false);
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
                    await heartbeat.SendPongAsync(connection, cancellationToken).ConfigureAwait(false);
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

    private async Task HandleSubscribeAsync(
        WebSocketConnection connection,
        Guid executionId,
        long? lastSequence,
        CancellationToken cancellationToken)
    {
        var userId = _authenticator.TryGetUserId();
        if (userId is null)
        {
            _logger.LogWarning(
                "Connection {ConnectionId} attempted to subscribe without authentication",
                connection.ConnectionId);
            return;
        }

        if (!await _authenticator.CanAccessExecutionAsync(
                userId.Value, executionId, Operation.Read, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning(
                "Connection {ConnectionId} denied subscription to execution {ExecutionId}",
                connection.ConnectionId, executionId);
            return;
        }

        _connectionManager.Subscribe(executionId, connection);
        _logger.LogInformation(
            "Connection {ConnectionId} subscribed to execution {ExecutionId}",
            connection.ConnectionId, executionId);

        if (lastSequence.HasValue)
        {
            await SendMissingEventsAsync(
                connection, executionId, lastSequence.Value, cancellationToken)
                .ConfigureAwait(false);
        }
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
            missingEvents = await _replayService.GetPersistedEventsAsync(executionId, lastSequence, cancellationToken)
                .ConfigureAwait(false);
        }

        if (missingEvents.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Replaying {Count} events for execution {ExecutionId} to connection {ConnectionId}",
            missingEvents.Count, executionId, connection.ConnectionId);

        foreach (var evt in missingEvents)
        {
            if (connection.WebSocket.State != WebSocketState.Open)
            {
                break;
            }

            var json = JsonSerializer.Serialize(evt, JsonOpts);
            var bytes = Encoding.UTF8.GetBytes(json);
            await connection.WebSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                cancellationToken).ConfigureAwait(false);
        }
    }
}
