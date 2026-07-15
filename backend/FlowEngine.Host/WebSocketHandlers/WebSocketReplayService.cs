using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Host.WebSocketHandlers;

/// <summary>
/// 事件重放服务，用于断线重连时补发缺失事件。
/// 不直接订阅 EventBus —— 由 WebSocketEventPushService 将已赋序号的事件推送过来存储。
/// 内存历史不可用时，从数据库执行记录重建事件。
/// 采用 LRU 策略限制缓存的 execution 数量，防止内存泄漏。
/// </summary>
public sealed class WebSocketReplayService : IDisposable
{
    private readonly ILogger<WebSocketReplayService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Dictionary<Guid, LinkedListNode<ExecutionEventCache>> _eventHistory = new();
    private readonly LinkedList<ExecutionEventCache> _lruList = new();
    private readonly object _lock = new();

    private const int MaxEventsPerExecution = 1000;
    private const int MaxExecutions = 100;

    public WebSocketReplayService(
        ILogger<WebSocketReplayService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// 记录一条已赋序号的事件，由推送服务调用。
    /// </summary>
    public void RecordEvent(Guid executionId, WebSocketPushMessage message)
    {
        lock (_lock)
        {
            if (!_eventHistory.TryGetValue(executionId, out var node))
            {
                // 新 execution，超过上限时 LRU 淘汰最久未访问的
                if (_eventHistory.Count >= MaxExecutions)
                {
                    var lru = _lruList.Last!;
                    _eventHistory.Remove(lru.Value.ExecutionId);
                    _lruList.RemoveLast();
                    _logger.LogDebug(
                        "LRU evicted execution {ExecutionId} to stay within limit {MaxExecutions}",
                        lru.Value.ExecutionId, MaxExecutions);
                }

                var cache = new ExecutionEventCache(executionId, new List<WebSocketPushMessage>());
                node = _lruList.AddFirst(cache);
                _eventHistory[executionId] = node;
            }
            else
            {
                // 移到 LRU 最前
                _lruList.Remove(node);
                _lruList.AddFirst(node);
            }

            node.Value.Events.Add(message);

            if (node.Value.Events.Count > MaxEventsPerExecution)
            {
                node.Value.Events.RemoveRange(0, node.Value.Events.Count - MaxEventsPerExecution);
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
            if (!_eventHistory.TryGetValue(executionId, out var node))
            {
                return Array.Empty<WebSocketPushMessage>();
            }

            // 移到 LRU 最前
            _lruList.Remove(node);
            _lruList.AddFirst(node);

            return node.Value.Events.Where(e => e.Sequence > lastSequence).ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// 从数据库执行记录重建事件列表，用于服务重启后内存历史丢失的场景。
    /// </summary>
    public async Task<IReadOnlyList<WebSocketPushMessage>> GetPersistedEventsAsync(
        Guid executionId,
        long lastSequence = 0,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();

        var record = await dbContext.ExecutionRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == executionId, cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            return Array.Empty<WebSocketPushMessage>();
        }

        var messages = new List<WebSocketPushMessage>();
        long sequence = 0;

        messages.Add(new WebSocketPushMessage
        {
            Type = "execution_started",
            ExecutionId = record.Id,
            Timestamp = record.StartedAt,
            Sequence = ++sequence,
            Payload = new
            {
                workflowDefinitionId = record.WorkflowDefinitionId,
                eventType = AuditEventTypes.ExecutionStarted,
            },
        });

        foreach (var nodeRecord in record.NodeRecords.OrderBy(n => n.StartedAt).ThenBy(n => n.CompletedAt))
        {
            var payload = BuildNodePayload(nodeRecord);
            messages.Add(new WebSocketPushMessage
            {
                Type = nodeRecord.Output.Success ? "node_executed" : "node_error",
                ExecutionId = record.Id,
                Timestamp = nodeRecord.CompletedAt ?? nodeRecord.StartedAt ?? record.StartedAt,
                Sequence = ++sequence,
                Payload = payload,
            });
        }

        if (record.Status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled
            && record.CompletedAt.HasValue)
        {
            var terminalType = record.Status switch
            {
                ExecutionStatus.Completed => "execution_completed",
                ExecutionStatus.Failed => "execution_failed",
                ExecutionStatus.Cancelled => "execution_cancelled",
                _ => throw new InvalidOperationException("Unexpected terminal status."),
            };

            messages.Add(new WebSocketPushMessage
            {
                Type = terminalType,
                ExecutionId = record.Id,
                Timestamp = record.CompletedAt.Value,
                Sequence = ++sequence,
                Payload = BuildTerminalPayload(record),
            });
        }

        var filtered = messages.Where(m => m.Sequence > lastSequence).ToList();

        _logger.LogInformation(
            "Rebuilt {Count} persisted events for execution {ExecutionId}",
            filtered.Count, executionId);

        return filtered.AsReadOnly();
    }

    private static object BuildNodePayload(NodeExecutionRecord nodeRecord)
    {
        var result = nodeRecord.Output;
        var error = result.Error;

        return new
        {
            nodeDefinitionId = nodeRecord.NodeDefinitionId,
            runIndex = nodeRecord.RunIndex,
            result = new
            {
                success = result.Success,
                itemCount = result.Output.Items.Count,
                error = error is not null
                    ? new { code = error.Code, message = error.Message }
                    : null,
            },
            eventType = result.Success ? AuditEventTypes.NodeExecuted : AuditEventTypes.NodeError,
        };
    }

    private static object BuildTerminalPayload(ExecutionRecord record)
    {
        var payload = new
        {
            workflowDefinitionId = record.WorkflowDefinitionId,
            finalStatus = record.Status.ToString(),
            eventType = record.Status switch
            {
                ExecutionStatus.Completed => AuditEventTypes.ExecutionCompleted,
                ExecutionStatus.Failed => AuditEventTypes.ExecutionFailed,
                ExecutionStatus.Cancelled => AuditEventTypes.ExecutionCancelled,
                _ => AuditEventTypes.ExecutionCompleted,
            },
        };

        return payload;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            _eventHistory.Clear();
            _lruList.Clear();
        }
    }

    private sealed record ExecutionEventCache(Guid ExecutionId, List<WebSocketPushMessage> Events);
}
