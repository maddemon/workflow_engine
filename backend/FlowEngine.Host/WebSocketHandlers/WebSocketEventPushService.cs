using System.Text.Json;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Events;

namespace FlowEngine.Host.WebSocketHandlers;

/// <summary>
/// 执行进度事件推送服务，订阅 EventBus 中的执行事件并转发到 WebSocket 连接。
/// 同时负责将事件存储到 WebSocketReplayService 用于断线重连补偿。
/// </summary>
public sealed class WebSocketEventPushService : IDisposable
{
    private static readonly System.Text.Json.JsonSerializerOptions SendJsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    private readonly IEventBus _eventBus;
    private readonly WebSocketConnectionManager _connectionManager;
    private readonly WebSocketReplayService _replayService;
    private readonly ILogger<WebSocketEventPushService> _logger;
    private long _sequenceCounter;
    private readonly List<IDisposable> _subscriptions = new();

    public WebSocketEventPushService(
        IEventBus eventBus,
        WebSocketConnectionManager connectionManager,
        WebSocketReplayService replayService,
        ILogger<WebSocketEventPushService> logger)
    {
        _eventBus = eventBus;
        _connectionManager = connectionManager;
        _replayService = replayService;
        _logger = logger;
        SubscribeToEvents();
    }

    private void SubscribeToEvents()
    {
        _subscriptions.Add(_eventBus.Subscribe<WorkflowStartedEvent>(OnWorkflowStartedAsync));
        _subscriptions.Add(_eventBus.Subscribe<NodeExecutedEvent>(OnNodeExecutedAsync));
        _subscriptions.Add(_eventBus.Subscribe<NodeErrorEvent>(OnNodeErrorAsync));
        _subscriptions.Add(_eventBus.Subscribe<WorkflowCompletedEvent>(OnWorkflowCompletedAsync));
        _subscriptions.Add(_eventBus.Subscribe<WorkflowFailedEvent>(OnWorkflowFailedAsync));
        _subscriptions.Add(_eventBus.Subscribe<WorkflowCancelledEvent>(OnWorkflowCancelledAsync));
    }

    private async Task OnWorkflowStartedAsync(WorkflowStartedEvent evt, CancellationToken cancellationToken)
    {
        var message = new WebSocketPushMessage
        {
            Type = "execution_started",
            ExecutionId = evt.ExecutionId,
            Timestamp = evt.OccurredAt,
            Sequence = Interlocked.Increment(ref _sequenceCounter),
            Payload = new
            {
                workflowDefinitionId = evt.WorkflowDefinitionId,
                eventType = evt.EventType,
            },
        };
        await BroadcastAndRecordAsync(evt.ExecutionId, message, cancellationToken).ConfigureAwait(false);
    }

    private async Task OnNodeExecutedAsync(NodeExecutedEvent evt, CancellationToken cancellationToken)
    {
        var message = new WebSocketPushMessage
        {
            Type = "node_executed",
            ExecutionId = evt.ExecutionId,
            Timestamp = evt.OccurredAt,
            Sequence = Interlocked.Increment(ref _sequenceCounter),
            Payload = BuildNodeExecutedPayload(evt),
        };
        await BroadcastAndRecordAsync(evt.ExecutionId, message, cancellationToken).ConfigureAwait(false);
    }

    private async Task OnNodeErrorAsync(NodeErrorEvent evt, CancellationToken cancellationToken)
    {
        var message = new WebSocketPushMessage
        {
            Type = "node_error",
            ExecutionId = evt.ExecutionId,
            Timestamp = evt.OccurredAt,
            Sequence = Interlocked.Increment(ref _sequenceCounter),
            Payload = new
            {
                nodeDefinitionId = evt.NodeDefinitionId,
                runIndex = evt.RunIndex,
                error = new
                {
                    code = evt.Error.Code,
                    message = evt.Error.Message,
                },
                eventType = evt.EventType,
            },
        };
        await BroadcastAndRecordAsync(evt.ExecutionId, message, cancellationToken).ConfigureAwait(false);
    }

    private async Task OnWorkflowCompletedAsync(WorkflowCompletedEvent evt, CancellationToken cancellationToken)
    {
        var message = new WebSocketPushMessage
        {
            Type = "execution_completed",
            ExecutionId = evt.ExecutionId,
            Timestamp = evt.OccurredAt,
            Sequence = Interlocked.Increment(ref _sequenceCounter),
            Payload = new
            {
                workflowDefinitionId = evt.WorkflowDefinitionId,
                finalStatus = evt.FinalStatus.ToString(),
                eventType = evt.EventType,
            },
        };
        await BroadcastAndRecordAsync(evt.ExecutionId, message, cancellationToken).ConfigureAwait(false);
    }

    private async Task OnWorkflowFailedAsync(WorkflowFailedEvent evt, CancellationToken cancellationToken)
    {
        var message = new WebSocketPushMessage
        {
            Type = "execution_failed",
            ExecutionId = evt.ExecutionId,
            Timestamp = evt.OccurredAt,
            Sequence = Interlocked.Increment(ref _sequenceCounter),
            Payload = new
            {
                workflowDefinitionId = evt.WorkflowDefinitionId,
                error = new
                {
                    code = evt.Error.Code,
                    message = evt.Error.Message,
                },
                eventType = evt.EventType,
            },
        };
        await BroadcastAndRecordAsync(evt.ExecutionId, message, cancellationToken).ConfigureAwait(false);
    }

    private async Task OnWorkflowCancelledAsync(WorkflowCancelledEvent evt, CancellationToken cancellationToken)
    {
        var message = new WebSocketPushMessage
        {
            Type = "execution_cancelled",
            ExecutionId = evt.ExecutionId,
            Timestamp = evt.OccurredAt,
            Sequence = Interlocked.Increment(ref _sequenceCounter),
            Payload = new
            {
                workflowDefinitionId = evt.WorkflowDefinitionId,
                eventType = evt.EventType,
            },
        };
        await BroadcastAndRecordAsync(evt.ExecutionId, message, cancellationToken).ConfigureAwait(false);
    }

    private static object BuildNodeExecutedPayload(NodeExecutedEvent evt)
    {
        var result = evt.Result;
        var outputSummary = new
        {
            success = result.Success,
            itemCount = result.Output.Items.Count,
            error = result.Error is not null
                ? new { code = result.Error.Code, message = result.Error.Message }
                : null,
        };

        return new
        {
            nodeDefinitionId = evt.NodeDefinitionId,
            runIndex = evt.RunIndex,
            result = outputSummary,
            eventType = evt.EventType,
        };
    }

    private async Task BroadcastAndRecordAsync(
        Guid executionId,
        WebSocketPushMessage message,
        CancellationToken cancellationToken)
    {
        _replayService.RecordEvent(executionId, message);
        await BroadcastAsync(executionId, message, cancellationToken).ConfigureAwait(false);
    }

    private async Task BroadcastAsync(Guid executionId, WebSocketPushMessage message, CancellationToken cancellationToken)
    {
        var connections = _connectionManager.GetConnections(executionId);
        if (connections.Count == 0)
        {
            return;
        }

        _logger.LogDebug(
            "Broadcasting {MessageType} to {Count} connections for execution {ExecutionId}",
            message.Type, connections.Count, executionId);

        var tasks = connections.Select(connection =>
            SendMessageSafeAsync(connection, message, cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task SendMessageSafeAsync(
        WebSocketConnection connection,
        WebSocketPushMessage message,
        CancellationToken cancellationToken)
    {
        if (connection.WebSocket.State != System.Net.WebSockets.WebSocketState.Open)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(message, SendJsonOptions);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            await connection.WebSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                System.Net.WebSockets.WebSocketMessageType.Text,
                true,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to send message to connection {ConnectionId}",
                connection.ConnectionId);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
        _subscriptions.Clear();
    }
}
