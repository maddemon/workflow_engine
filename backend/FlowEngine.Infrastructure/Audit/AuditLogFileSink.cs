using System.Text.Json;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Events;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Infrastructure.Audit;

/// <summary>
/// 审计日志文件 Sink，订阅 EventBus 事件并写入 NDJSON 文件。
/// 普通事件异步入队，后台每秒批量刷盘；关键事件同步刷盘。
/// </summary>
public sealed class AuditLogFileSink : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _logDirectory;
    private readonly ILogger<AuditLogFileSink>? _logger;
    private readonly Lock _writerLock = new();
    private StreamWriter? _writer;
    private string _currentDate = string.Empty;
    private Timer? _flushTimer;
    private bool _disposed;

    /// <summary>
    /// 初始化审计日志文件 Sink。
    /// </summary>
    /// <param name="logDirectory">日志目录路径。</param>
    /// <param name="eventBus">事件总线，用于订阅审计事件。</param>
    /// <param name="logger">可选日志记录器。</param>
    public AuditLogFileSink(
        string logDirectory,
        IEventBus eventBus,
        ILogger<AuditLogFileSink>? logger = null)
    {
        _logDirectory = logDirectory;
        _logger = logger;

        Directory.CreateDirectory(logDirectory);
        EnsureWriter();

        _flushTimer = new Timer(_ => Flush(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        eventBus.Subscribe<IDomainEvent>((e, _) =>
        {
            OnEvent(e);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 处理事件：写入 NDJSON 行，关键事件同步刷盘。
    /// </summary>
    /// <param name="event">领域事件。</param>
    public void OnEvent(IDomainEvent @event)
    {
        if (_disposed)
        {
            return;
        }

        var line = SerializeEvent(@event);
        if (line is null)
        {
            return;
        }

        lock (_writerLock)
        {
            EnsureWriter();
            _writer?.WriteLine(line);

            if (IsCriticalEvent(@event))
            {
                _writer?.Flush();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _flushTimer?.Dispose();
        _flushTimer = null;

        lock (_writerLock)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void EnsureWriter()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (today == _currentDate && _writer is not null)
        {
            return;
        }

        _writer?.Flush();
        _writer?.Dispose();

        var filePath = Path.Combine(_logDirectory, $"audit-{today}.ndjson");
        _writer = new StreamWriter(filePath, append: true) { AutoFlush = false };
        _currentDate = today;
    }

    private void Flush()
    {
        if (_disposed)
        {
            return;
        }

        lock (_writerLock)
        {
            try
            {
                _writer?.Flush();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to flush audit log");
            }
        }
    }

    private static string? SerializeEvent(IDomainEvent @event)
    {
        try
        {
            if (@event is AuditEvent audit)
            {
                return JsonSerializer.Serialize(new
                {
                    id = audit.EventId,
                    eventType = audit.EventType,
                    timestamp = audit.OccurredAt,
                    actor = audit.Actor,
                    resourceType = audit.ResourceType,
                    resourceId = audit.ResourceId,
                    payload = audit.Payload,
                    metadata = audit.Metadata,
                }, JsonOptions);
            }

            return JsonSerializer.Serialize(new
            {
                id = @event.EventId,
                eventType = @event.GetType().Name,
                timestamp = @event.OccurredAt,
            }, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsCriticalEvent(IDomainEvent @event)
    {
        if (@event is AuditEvent audit && !string.IsNullOrEmpty(audit.EventType))
        {
            return AuditEventTypes.CriticalEvents.Contains(audit.EventType);
        }

        return false;
    }
}
