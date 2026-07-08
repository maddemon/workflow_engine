using System.Threading.Channels;
using System.Text.Json;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Events;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Infrastructure.Audit;

/// <summary>
/// 审计日志文件 Sink，订阅 EventBus 事件并写入 NDJSON 文件。
/// 所有事件先入队，再由后台任务批量刷盘，避免阻塞发布线程。
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
    private readonly Channel<AuditEvent> _channel;
    private readonly CancellationTokenSource _cts;
    private readonly Task _processor;
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
        _channel = Channel.CreateUnbounded<AuditEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        _cts = new CancellationTokenSource();

        Directory.CreateDirectory(logDirectory);
        EnsureWriter();

        eventBus.Subscribe<AuditEvent>(OnEventAsync);
        _processor = Task.Run(ProcessLoopAsync);
        _flushTimer = new Timer(_ => Flush(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// 异步处理审计事件：写入队列，立即返回，不阻塞发布线程。
    /// </summary>
    /// <param name="auditEvent">审计事件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task OnEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        if (!_channel.Writer.TryWrite(auditEvent))
        {
            _logger?.LogError("审计事件入队失败，事件可能丢失: {EventType}", auditEvent.EventType);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.Complete();
        _cts.Cancel();
        _flushTimer?.Dispose();
        _flushTimer = null;

        try
        {
            _processor.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "审计日志后台任务等待时发生异常");
        }

        _cts.Dispose();

        lock (_writerLock)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }

    private async Task ProcessLoopAsync()
    {
        await foreach (var auditEvent in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
        {
            try
            {
                await WriteEventAsync(auditEvent, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "写入审计事件失败: {EventType}", auditEvent.EventType);
            }
        }
    }

    private async Task WriteEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        var line = SerializeEvent(auditEvent);
        if (line is null)
        {
            return;
        }

        lock (_writerLock)
        {
            EnsureWriter();
            _writer?.WriteLine(line);
        }

        if (IsCriticalEvent(auditEvent))
        {
            lock (_writerLock)
            {
                _writer?.Flush();
            }
        }
        else
        {
            // 非关键事件批量刷盘：每 1 秒或队列空闲时由操作系统/StreamWriter 缓冲。
            // 取消时立即 flush，由 Dispose 处理。
        }

        await Task.CompletedTask;
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

    private static string? SerializeEvent(AuditEvent audit)
    {
        try
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
        catch
        {
            return null;
        }
    }

    private static bool IsCriticalEvent(AuditEvent audit)
    {
        return !string.IsNullOrEmpty(audit.EventType)
            && AuditEventTypes.CriticalEvents.Contains(audit.EventType);
    }
}
