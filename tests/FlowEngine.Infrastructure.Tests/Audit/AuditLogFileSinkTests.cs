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
