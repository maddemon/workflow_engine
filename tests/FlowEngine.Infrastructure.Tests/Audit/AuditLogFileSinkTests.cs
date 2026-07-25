using System;
using FlowEngine.Application.Audit;
using FlowEngine.Core.Events;
using FlowEngine.Infrastructure.Audit;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Infrastructure.Tests.Audit;

[Collection("AuditLogFileSink")]
public sealed class AuditLogFileSinkTests : IDisposable
{
    private readonly string _logDirectory;

    public AuditLogFileSinkTests()
    {
        _logDirectory = Path.Combine(Path.GetTempPath(), $"flowengine-sink-{Guid.NewGuid():N}");
        AuditNetBootstrap.EnsureConfigured();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_logDirectory))
            {
                Directory.Delete(_logDirectory, recursive: true);
            }
        }
        catch
        {
            // 临时目录清理失败不影响测试结果。
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AuditLogEvent CreateEvent(string eventType)
    {
        return new AuditLogEvent
        {
            EventType = eventType,
            Actor = "system",
            ResourceType = "Workflow",
            ResourceId = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow,
        };
    }

    [Fact]
    public void Constructor_CreatesLogDirectory()
    {
        using var sink = new AuditLogFileSink(_logDirectory, NullLogger<AuditLogFileSink>.Instance);

        Assert.True(Directory.Exists(_logDirectory));
    }

    [Fact]
    public async Task OnEventAsync_CriticalEvent_WritesToFile()
    {
        var sink = new AuditLogFileSink(_logDirectory, NullLogger<AuditLogFileSink>.Instance);
        var e = CreateEvent(AuditEventTypes.CredentialAccessed);

        await sink.OnEventAsync(e, Ct);
        sink.Dispose();

        var filePath = await WaitForAuditFileAsync(Ct);
        var lines = await File.ReadAllLinesAsync(filePath, Ct);
        Assert.Single(lines);
        Assert.Contains(AuditEventTypes.CredentialAccessed, lines[0]);
    }

    private async Task<string> WaitForAuditFileAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(10000);
        while (DateTime.UtcNow < deadline)
        {
            var files = Directory.GetFiles(_logDirectory, "audit-*.ndjson");
            if (files.Length == 1)
            {
                var lines = await File.ReadAllLinesAsync(files[0], ct).ConfigureAwait(false);
                if (lines.Length > 0)
                {
                    return files[0];
                }
            }

            await Task.Delay(50, ct).ConfigureAwait(false);
        }

        Assert.Fail("审计事件在超时内未落盘。");
        return string.Empty;
    }

    private async Task<string> WaitForDeadLetterAsync(Guid eventId, CancellationToken ct)
    {
        var deadLetterDir = Path.Combine(_logDirectory, "deadletter");
        var deadline = DateTime.UtcNow.AddMilliseconds(10000);
        while (DateTime.UtcNow < deadline)
        {
            if (Directory.Exists(deadLetterDir))
            {
                foreach (var file in Directory.GetFiles(deadLetterDir, "audit-deadletter-*.ndjson"))
                {
                    var content = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                    if (content.Contains(eventId.ToString(), StringComparison.Ordinal))
                    {
                        return file;
                    }
                }
            }

            await Task.Delay(50, ct).ConfigureAwait(false);
        }

        Assert.Fail("dead-letter file not generated within timeout.");
        return string.Empty;
    }

    [Fact]
    public async Task OnEventAsync_SerializationFailure_WritesDeadLetterInsteadOfSilentDrop()
    {
        // E-1: a serialization failure (simulated via an injected throwing serializer) must not
        // be silently dropped; it is written to the dead-letter file and never reaches the main audit file.
        static string? FailingSerializer(AuditEvent _) => throw new InvalidOperationException("serialize boom");
        var sink = new AuditLogFileSink(_logDirectory, NullLogger<AuditLogFileSink>.Instance, FailingSerializer);
        var e = new CredentialAccessedEvent(Guid.NewGuid(), Guid.NewGuid(), "node-def-1", "Resolve");

        await sink.OnEventAsync(e, Ct);
        var deadLetterPath = await WaitForDeadLetterAsync(e.EventId, Ct);
        sink.Dispose();

        var content = await File.ReadAllTextAsync(deadLetterPath, Ct);
        Assert.Contains(e.EventType, content);
        Assert.Contains(e.EventId.ToString(), content);

        // 主审计文件（由 EnsureWriter 预创建，可能为空）不得包含该事件 ID：
        // 事件已转入死信而非混入主日志或静默丢失。
        foreach (var file in Directory.GetFiles(_logDirectory, "audit-*.ndjson"))
        {
            var mainContent = await File.ReadAllTextAsync(file, Ct);
            Assert.DoesNotContain(e.EventId.ToString(), mainContent);
        }
    }

    [Fact]
    public async Task OnEventAsync_NonCriticalEvent_IsFlushedByTimer()
    {
        var sink = new AuditLogFileSink(_logDirectory, NullLogger<AuditLogFileSink>.Instance);
        var e = CreateEvent(AuditEventTypes.WorkflowCreated);

        await sink.OnEventAsync(e, Ct);
        await Task.Delay(TimeSpan.FromMilliseconds(1200), Ct);
        sink.Dispose();

        var filePath = Directory.GetFiles(_logDirectory, "audit-*.ndjson").Single();
        var lines = await File.ReadAllLinesAsync(filePath, Ct);
        Assert.Single(lines);
        Assert.Contains(AuditEventTypes.WorkflowCreated, lines[0]);
    }

    [Fact]
    public async Task OnEventAsync_AfterDispose_ReturnsCompletedTask()
    {
        var sink = new AuditLogFileSink(_logDirectory, NullLogger<AuditLogFileSink>.Instance);
        sink.Dispose();

        var task = sink.OnEventAsync(CreateEvent(AuditEventTypes.WorkflowCreated), Ct);

        Assert.Equal(Task.CompletedTask, task);
    }

    [Fact]
    public async Task StopAsync_IsIdempotent()
    {
        var sink = new AuditLogFileSink(_logDirectory, NullLogger<AuditLogFileSink>.Instance);

        await sink.StopAsync(Ct);
        await sink.StopAsync(Ct);

        Assert.True(Directory.Exists(_logDirectory));
        sink.Dispose();
    }

    [Fact]
    public async Task StartAsync_ReturnsCompletedTask()
    {
        using var sink = new AuditLogFileSink(_logDirectory, NullLogger<AuditLogFileSink>.Instance);

        var task = sink.StartAsync(Ct);

        Assert.Equal(Task.CompletedTask, task);
    }

    [Fact]
    public async Task MultipleEvents_AreAppended()
    {
        var sink = new AuditLogFileSink(_logDirectory, NullLogger<AuditLogFileSink>.Instance);

        await sink.OnEventAsync(CreateEvent(AuditEventTypes.CredentialAccessed), Ct);
        await sink.OnEventAsync(CreateEvent(AuditEventTypes.CredentialDeleted), Ct);
        await Task.Delay(TimeSpan.FromMilliseconds(100), Ct);
        sink.Dispose();

        var filePath = Directory.GetFiles(_logDirectory, "audit-*.ndjson").Single();
        var lines = await File.ReadAllLinesAsync(filePath, Ct);
        Assert.Equal(2, lines.Length);
    }
}

[CollectionDefinition("AuditLogFileSink", DisableParallelization = true)]
public sealed class AuditLogFileSinkCollection : ICollectionFixture<object>
{
}
