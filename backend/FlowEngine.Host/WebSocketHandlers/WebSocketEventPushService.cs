using System.Text.Json;
using FlowEngine.Core.Events;
using MediatR;

namespace FlowEngine.Host.WebSocketHandlers;

/// <summary>
/// 执行进度事件推送服务，将执行事件转发到 WebSocket 连接，并存储到 WebSocketReplayService 用于断线重连补偿。
/// 通过实现 <see cref="INotificationHandler{TNotification}"/> 消费 MediatR 发布的领域事件
/// （替代原通过 <c>IEventBus.Subscribe</c> 的订阅，见任务 2.1：事件总线 → MediatR）。
/// </summary>
public sealed class WebSocketEventPushService(
    WebSocketConnectionManager connectionManager,
    WebSocketReplayService replayService,
    ILogger<WebSocketEventPushService> logger) :
    IDisposable,
    INotificationHandler<WorkflowStartedEvent>,
    INotificationHandler<NodeStartedEvent>,
    INotificationHandler<NodeExecutedEvent>,
    INotificationHandler<NodeErrorEvent>,
    INotificationHandler<WorkflowCompletedEvent>,
    INotificationHandler<WorkflowFailedEvent>,
    INotificationHandler<WorkflowCancelledEvent>,
    INotificationHandler<LlmTokenStreamEvent>
{
    private static readonly System.Text.Json.JsonSerializerOptions SendJsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    private readonly WebSocketConnectionManager _connectionManager = connectionManager;
    private readonly WebSocketReplayService _replayService = replayService;
    private readonly ILogger<WebSocketEventPushService> _logger = logger;
    private long _sequenceCounter;

    /// <inheritdoc />
    public async Task Handle(WorkflowStartedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            await OnWorkflowStartedAsync(notification, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "推送 WorkflowStartedEvent 失败，execution={ExecutionId}", notification.ExecutionId);
        }
    }

    /// <inheritdoc />
    public async Task Handle(NodeStartedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            await OnNodeStartedAsync(notification, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "推送 NodeStartedEvent 失败，execution={ExecutionId}", notification.ExecutionId);
        }
    }

    /// <inheritdoc />
    public async Task Handle(NodeExecutedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            await OnNodeExecutedAsync(notification, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "推送 NodeExecutedEvent 失败，execution={ExecutionId}", notification.ExecutionId);
        }
    }

    /// <inheritdoc />
    public async Task Handle(NodeErrorEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            await OnNodeErrorAsync(notification, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "推送 NodeErrorEvent 失败，execution={ExecutionId}", notification.ExecutionId);
        }
    }

    /// <inheritdoc />
    public async Task Handle(WorkflowCompletedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            await OnWorkflowCompletedAsync(notification, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "推送 WorkflowCompletedEvent 失败，execution={ExecutionId}", notification.ExecutionId);
        }
    }

    /// <inheritdoc />
    public async Task Handle(WorkflowFailedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            await OnWorkflowFailedAsync(notification, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "推送 WorkflowFailedEvent 失败，execution={ExecutionId}", notification.ExecutionId);
        }
    }

    /// <inheritdoc />
    public async Task Handle(WorkflowCancelledEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            await OnWorkflowCancelledAsync(notification, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "推送 WorkflowCancelledEvent 失败，execution={ExecutionId}", notification.ExecutionId);
        }
    }

    /// <inheritdoc />
    public async Task Handle(LlmTokenStreamEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            await OnLlmTokenStreamAsync(notification, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "推送 LlmTokenStreamEvent 失败，execution={ExecutionId}", notification.ExecutionId);
        }
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

    private async Task OnNodeStartedAsync(NodeStartedEvent evt, CancellationToken cancellationToken)
    {
        var message = new WebSocketPushMessage
        {
            Type = "node_started",
            ExecutionId = evt.ExecutionId,
            Timestamp = evt.OccurredAt,
            Sequence = Interlocked.Increment(ref _sequenceCounter),
            Payload = new
            {
                nodeDefinitionId = evt.NodeDefinitionId,
                runIndex = evt.RunIndex,
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
                    code = evt.Error?.Code ?? string.Empty,
                    message = evt.Error?.Message ?? string.Empty,
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

    private async Task OnLlmTokenStreamAsync(LlmTokenStreamEvent evt, CancellationToken cancellationToken)
    {
        // LLM token 流事件高频、数据量大，且重连后只需恢复最终节点输出，
        // 因此使用 BroadcastAsync 直接推送而不写入 replay 缓存，以降低内存压力。
        var message = new WebSocketPushMessage
        {
            Type = "llm_token",
            ExecutionId = evt.ExecutionId,
            Timestamp = evt.OccurredAt,
            Sequence = Interlocked.Increment(ref _sequenceCounter),
            Payload = new
            {
                nodeDefinitionId = evt.NodeDefinitionId,
                runIndex = evt.RunIndex,
                delta = evt.Delta,
                isFinal = evt.IsFinal,
                eventType = evt.EventType,
            },
        };
        await BroadcastAsync(evt.ExecutionId, message, cancellationToken).ConfigureAwait(false);
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
        // 事件消费现由 MediatR 处理器完成，无需注销订阅。
    }
}
