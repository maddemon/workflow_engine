using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Identity;
using Microsoft.AspNetCore.Http;

namespace FlowEngine.Host.WebSocketHandlers;

/// <summary>
/// 执行进度 WebSocket 端点处理器。
/// </summary>
public sealed class ExecutionWebSocketHandler
{
    private readonly WebSocketConnectionManager _connectionManager;
    private readonly ILogger<ExecutionWebSocketHandler> _logger;
    private readonly WebSocketAuthenticator _authenticator;
    private readonly WebSocketSubscriptionManager _subscriptions;
    private readonly WebSocketHeartbeatHandler _heartbeat;
    private readonly ArrayPool<byte> _bufferPool;

    /// <summary>
    /// 初始化执行 WebSocket 端点处理器。
    /// </summary>
    /// <param name="bufferPool">接收缓冲区池，默认 <see cref="ArrayPool{T}.Shared"/>；测试可注入自定义池以校验归还次数（CON-1）。</param>
    public ExecutionWebSocketHandler(
        WebSocketConnectionManager connectionManager,
        WebSocketReplayService replayService,
        IUserContext userContext,
        IResourceAuthorizationService resourceAuthorization,
        ILogger<ExecutionWebSocketHandler> logger,
        ArrayPool<byte>? bufferPool = null)
    {
        _connectionManager = connectionManager;
        _logger = logger;
        _bufferPool = bufferPool ?? ArrayPool<byte>.Shared;
        _authenticator = new WebSocketAuthenticator(userContext, resourceAuthorization);
        _subscriptions = new WebSocketSubscriptionManager(
            connectionManager, replayService, _authenticator, logger);
        _heartbeat = new WebSocketHeartbeatHandler(logger);
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
            await next().ConfigureAwait(false);
            return;
        }

        if (!_authenticator.IsAuthenticated)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var webSocket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        var connection = new WebSocketConnection(webSocket)
        {
            UserId = _authenticator.UserId,
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
        var buffer = _bufferPool.Rent(4096);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatCts = new CancellationTokenSource();
        cts.Token.Register(() => heartbeatCts.Cancel());

        _ = _heartbeat.RunHeartbeatAsync(connection, heartbeatCts.Token);

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
                    // 大消息处理：若非 EndOfMessage，用 MemoryStream 动态扩容拼接。
                    string message;
                    if (result.EndOfMessage)
                    {
                        message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    }
                    else
                    {
                        using var ms = new MemoryStream();
                        ms.Write(buffer, 0, result.Count);

                        while (true)
                        {
                            var chunk = await connection.WebSocket.ReceiveAsync(
                                new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);

                            if (chunk.MessageType == WebSocketMessageType.Close)
                            {
                                // 大消息中间收到 Close：直接退出，buffer 由下方 finally 独占归还，
                                // 避免与 finally 中的 Return 形成双重归还污染 ArrayPool（CON-1）。
                                return;
                            }

                            ms.Write(buffer, 0, chunk.Count);

                            if (chunk.EndOfMessage)
                            {
                                break;
                            }
                        }

                        message = Encoding.UTF8.GetString(ms.GetBuffer().AsSpan(0, (int)ms.Length));
                    }

                    await _subscriptions.HandleMessageAsync(connection, message, _heartbeat, cts.Token)
                        .ConfigureAwait(false);
                }

                connection.LastActivityAt = DateTime.UtcNow;
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        finally
        {
            _bufferPool.Return(buffer);
            heartbeatCts.Cancel();
            heartbeatCts.Dispose();
        }
    }
}
