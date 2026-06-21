using System.Text.Json;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Events;

namespace FlowEngine.Host.WebSocketHandlers;

/// <summary>
/// 事件重放服务，用于断线重连时补发缺失事件。
/// </summary>
public sealed class WebSocketReplayService
{
    private readonly IEventBus _eventBus;
    private readonly IUserContext _userContext;
    private readonly ILogger<WebSocketReplayService> _logger;
    private readonly List<IDisposable> _subscriptions = new();
    private long _sequenceCounter;
    private readonly Dictionary<Guid, List<WebSocketPushMessage>> _eventHistory = new();
    private readonly object _lock = new();

    /// <summary>
    /// 初始化事件重放服务。
    /// </summary>
    public WebSocketReplayService(
        IEventBus eventBus,
        IUserContext userContext,
        ILogger<WebSocketReplayService> logger)
    {
        _eventBus = eventBus;
        _userContext = userContext;
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
        RecordEvent(evt.ExecutionId, new WebSocketPushMessage
        {
            Type = "execution_started",
            ExecutionId = evt.ExecutionId,
            Timestamp = evt.OccurredAt,
            Sequence = Interlocked.Increment(ref _sequenceCounter),
            Payload = new { workflowDefinitionId = evt.WorkflowDefinitionId, eventType = evt.EventType },
        });
        await Task.CompletedTask;
    }

    private async Task OnNodeExecutedAsync(NodeExecutedEvent evt, CancellationToken cancellationToken)
    {
        RecordEvent(evt.ExecutionId, new WebSocketPushMessage
        {
            Type = "node_executed",
            ExecutionId = evt.ExecutionId,
            Timestamp = evt.OccurredAt,
            Sequence = Interlocked.Increment(ref _sequenceCounter),
            Payload = new
            {
                nodeDefinitionId = evt.NodeDefinitionId,
                runIndex = evt.RunIndex,
                result = new
                {
                    success = evt.Result.Success,
                    itemCount = evt.Result.Output.Items.Count,
                },
                eventType = evt.EventType,
            },
        });
        await Task.CompletedTask;
    }

    private async Task OnNodeErrorAsync(NodeErrorEvent evt, CancellationToken cancellationToken)
    {
        RecordEvent(evt.ExecutionId, new WebSocketPushMessage
        {
            Type = "node_error",
            ExecutionId = evt.ExecutionId,
            Timestamp = evt.OccurredAt,
            Sequence = Interlocked.Increment(ref _sequenceCounter),
            Payload = new
            {
                nodeDefinitionId = evt.NodeDefinitionId,
                runIndex = evt.RunIndex,
                error = new { code = evt.Error.Code, message = evt.Error.Message },
                eventType = evt.EventType,
            },
        });
        await Task.CompletedTask;
    }

    private async Task OnWorkflowCompletedAsync(WorkflowCompletedEvent evt, CancellationToken cancellationToken)
    {
        RecordEvent(evt.ExecutionId, new WebSocketPushMessage
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
        });
        await Task.CompletedTask;
    }

    private async Task OnWorkflowFailedAsync(WorkflowFailedEvent evt, CancellationToken cancellationToken)
    {
        RecordEvent(evt.ExecutionId, new WebSocketPushMessage
        {
            Type = "execution_failed",
            ExecutionId = evt.ExecutionId,
            Timestamp = evt.OccurredAt,
            Sequence = Interlocked.Increment(ref _sequenceCounter),
            Payload = new
            {
                workflowDefinitionId = evt.WorkflowDefinitionId,
                error = new { code = evt.Error.Code, message = evt.Error.Message },
                eventType = evt.EventType,
            },
        });
        await Task.CompletedTask;
    }

    private async Task OnWorkflowCancelledAsync(WorkflowCancelledEvent evt, CancellationToken cancellationToken)
    {
        RecordEvent(evt.ExecutionId, new WebSocketPushMessage
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
        });
        await Task.CompletedTask;
    }

    private void RecordEvent(Guid executionId, WebSocketPushMessage message)
    {
        lock (_lock)
        {
            if (!_eventHistory.TryGetValue(executionId, out var events))
            {
                events = new List<WebSocketPushMessage>();
                _eventHistory[executionId] = events;
            }
            events.Add(message);
        }

        _logger.LogDebug(
            "Recorded event {Type} for execution {ExecutionId}, sequence {Sequence}",
            message.Type, executionId, message.Sequence);
    }

    /// <summary>
    /// 获取指定执行 ID 的缺失事件（从 lastSequence 之后的事件）。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="lastSequence">上次接收的事件序号。</param>
    /// <returns>需要补发的事件列表。</returns>
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
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
        _subscriptions.Clear();

        lock (_lock)
        {
            _eventHistory.Clear();
        }
    }
}
