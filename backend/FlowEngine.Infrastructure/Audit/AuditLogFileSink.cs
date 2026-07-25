using System.Threading;
using System.Threading.Channels;
using FlowEngine.Core.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Infrastructure.Audit;

/// <summary>
/// 审计日志文件 Sink，接收审计事件并写入 NDJSON 文件。
/// 事件由对应的 MediatR 通知处理器（<see cref="AuditEventNotificationHandler"/>）转发至此。
/// 所有事件先入队，再由后台任务批量刷盘，避免阻塞发布线程。
/// 作为托管服务（IHostedService）随宿主启动/停止，避免“为副作用而解析”。
/// </summary>
public sealed class AuditLogFileSink : IHostedService, IDisposable
{
    private readonly string _logDirectory;
    private readonly ILogger<AuditLogFileSink>? _logger;
    private readonly Func<AuditEvent, string?>? _serializer;
    private readonly Lock _writerLock = new();
    private readonly Channel<AuditEvent> _channel;
    private readonly CancellationTokenSource _cts;
    private readonly Task _processor;
    private StreamWriter? _writer;
    private string _currentDate = string.Empty;
    private Timer? _flushTimer;
    private int _disposed;

    /// <summary>
    /// 初始化审计日志文件 Sink。
    /// </summary>
    /// <param name="logDirectory">日志目录路径。</param>
    /// <param name="logger">可选日志记录器。</param>
    /// <param name="serializer">可选序列化器（主要用于测试注入可抛异常的序列化以验证死信逻辑）；为 null 时使用默认 NDJSON 序列化。</param>
    public AuditLogFileSink(
        string logDirectory,
        ILogger<AuditLogFileSink>? logger = null,
        Func<AuditEvent, string?>? serializer = null)
    {
        _logDirectory = logDirectory;
        _logger = logger;
        _serializer = serializer;

        // 任务 2.2：确保 Audit.NET 已配置（序列化适配器注册为进程级全局状态，幂等）。
        // 这样即使 Sink 在 DI 容器之外被直接构造（如单元测试），也能获得正确的 NDJSON 序列化。
        AuditNetBootstrap.EnsureConfigured();

        _channel = Channel.CreateUnbounded<AuditEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        _cts = new CancellationTokenSource();

        Directory.CreateDirectory(logDirectory);
        EnsureWriter();

        _processor = Task.Run(ProcessLoopAsync);
        _flushTimer = new Timer(_ => Flush(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// 托管服务启动入口。后台刷盘循环已在构造函数中启动，此处无需额外操作。
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// 优雅停止审计刷盘任务并释放资源（托管服务停止时由宿主调用）。
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Shutdown("审计日志后台任务停止时发生异常");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步处理审计事件：写入队列，立即返回，不阻塞发布线程。
    /// </summary>
    /// <param name="auditEvent">审计事件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task OnEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0)
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
        Shutdown("审计日志后台任务等待时发生异常");
    }

    private void Shutdown(string errorMessage)
    {
        // 确保 StopAsync 与 Dispose 的清理序列只执行一次，避免 double-complete / double-dispose。
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        _channel.Writer.Complete();
        _flushTimer?.Dispose();
        _flushTimer = null;

        try
        {
            // 先等待后台任务自然排空通道（通道已完成，处理完剩余事件后会自行退出）。
            // 若超时仍未退出，再取消令牌强制终止，避免在事件尚未处理前就被取消导致 flaky。
            if (!_processor.Wait(TimeSpan.FromSeconds(5)))
            {
                _cts.Cancel();
                _processor.Wait(TimeSpan.FromSeconds(1));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, errorMessage);
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
            // E-1：序列化失败——记录错误日志并将事件写入死信，避免审计事件静默丢失。
            _logger?.LogError(
                "审计事件序列化失败，已写入死信文件: {EventType} (EventId={EventId})",
                auditEvent.EventType,
                auditEvent.EventId);
            WriteDeadLetter(auditEvent);
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
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0)
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
        // 使用 FileShare.ReadWrite 打开，避免文件被其他进程（或上一次未退出的宿主）持有时启动失败。
        var fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(fileStream, leaveOpen: false) { AutoFlush = false };
        _currentDate = today;
    }

    private string? SerializeEvent(AuditEvent audit)
    {
        try
        {
            // 测试注入的序列化器优先（主要用于验证 E-1 死信逻辑）：其抛异常或返回 null 均会
            // 触发死信落盘，而非静默丢弃事件。
            if (_serializer is not null)
            {
                return _serializer(audit);
            }

            // 将领域 AuditEvent 映射到 Audit.NET 事件模型，并以 Audit.NET 的可插拔
            // JsonAdapter（FlowEngineAuditJsonAdapter）序列化为与历史 NDJSON 完全一致的字段布局。
            // 文件轮转、追加与后台刷盘逻辑保持不变。
            var mapped = new FlowEngineAuditEvent
            {
                Id = audit.EventId,
                EventType = audit.EventType,
                Timestamp = audit.OccurredAt,
                Actor = audit.Actor,
                ResourceType = audit.ResourceType,
                ResourceId = audit.ResourceId,
                Payload = audit.Payload,
                Metadata = audit.Metadata,
            };

            return global::Audit.Core.Configuration.JsonAdapter.Serialize(mapped);
        }
        catch (Exception)
        {
            // E-1：序列化失败不再静默丢弃事件。返回 null，由 WriteEventAsync 记录错误日志并写入死信。
            return null;
        }
    }

    /// <summary>
    /// 将序列化失败的事件写入死信文件（不记录任何敏感负载，仅保留事件类型与时间戳以便排查）。
    /// 死信文件位于审计日志目录下的 <c>deadletter/</c> 子目录，按日滚动。
    /// </summary>
    private void WriteDeadLetter(AuditEvent audit)
    {
        try
        {
            var deadLetterDir = Path.Combine(_logDirectory, "deadletter");
            Directory.CreateDirectory(deadLetterDir);

            var record = new
            {
                eventType = audit.EventType,
                occurredAt = audit.OccurredAt,
                eventId = audit.EventId,
            };

            var line = System.Text.Json.JsonSerializer.Serialize(record);
            var filePath = Path.Combine(deadLetterDir, $"audit-deadletter-{DateTime.UtcNow:yyyy-MM-dd}.ndjson");
            lock (_writerLock)
            {
                File.AppendAllLines(filePath, [line]);
            }
        }
        catch
        {
            // 死信写入本身失败（如磁盘不可写）时静默忽略，绝不向上抛出影响主流程。
        }
    }

    private static bool IsCriticalEvent(AuditEvent audit)
    {
        return !string.IsNullOrEmpty(audit.EventType)
            && AuditEventTypes.CriticalEvents.Contains(audit.EventType);
    }
}
