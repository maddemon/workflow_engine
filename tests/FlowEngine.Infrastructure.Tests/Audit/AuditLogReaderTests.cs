using System.Text.Json;
using FlowEngine.Application.Audit;
using FlowEngine.Infrastructure.Audit;
using Xunit;

namespace FlowEngine.Infrastructure.Tests.Audit;

public sealed class AuditLogReaderTests : IDisposable
{
    private readonly string _logDirectory;
    private readonly AuditLogReader _reader;

    public AuditLogReaderTests()
    {
        _logDirectory = Path.Combine(Path.GetTempPath(), $"flowengine-audit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_logDirectory);
        _reader = new AuditLogReader(_logDirectory);
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

    private static string EventLine(
        string eventType,
        DateTime timestamp,
        string resourceType = "Workflow",
        Guid? resourceId = null,
        string actor = "system")
    {
        var doc = new
        {
            id = Guid.NewGuid(),
            eventType,
            timestamp,
            actor,
            resourceType,
            resourceId = resourceId ?? Guid.NewGuid(),
            payload = (Dictionary<string, object>?)null,
            metadata = (Dictionary<string, string>?)null,
        };
        return JsonSerializer.Serialize(doc);
    }

    [Fact]
    public async Task QueryAsync_EmptyDirectory_ReturnsEmptyResult()
    {
        var result = await _reader.QueryAsync(new AuditQueryParameters(), Ct);

        Assert.Empty(result.Events);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task QueryAsync_NoLogDirectory_ReturnsEmptyResult()
    {
        var missingDir = Path.Combine(Path.GetTempPath(), $"flowengine-audit-missing-{Guid.NewGuid():N}");
        var reader = new AuditLogReader(missingDir);

        var result = await reader.QueryAsync(new AuditQueryParameters(), Ct);

        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task QueryAsync_WithEvents_ReturnsEventsDescending()
    {
        var t1 = DateTime.UtcNow.AddMinutes(-10);
        var t2 = DateTime.UtcNow.AddMinutes(-5);
        var t3 = DateTime.UtcNow;
        await File.WriteAllLinesAsync(
            Path.Combine(_logDirectory, $"audit-{DateTime.UtcNow:yyyy-MM-dd}.ndjson"),
            [EventLine("Workflow.Created", t1), EventLine("Workflow.Updated", t2), EventLine("Workflow.Deleted", t3)],
            Ct);

        var result = await _reader.QueryAsync(new AuditQueryParameters { Limit = 10 }, Ct);

        Assert.Equal(3, result.Total);
        Assert.Equal(3, result.Events.Count);
        Assert.Equal("Workflow.Deleted", result.Events[0].RootElement.GetProperty("eventType").GetString());
        Assert.Equal("Workflow.Updated", result.Events[1].RootElement.GetProperty("eventType").GetString());
        Assert.Equal("Workflow.Created", result.Events[2].RootElement.GetProperty("eventType").GetString());
    }

    [Fact]
    public async Task QueryAsync_ByEventType_Filters()
    {
        await File.WriteAllLinesAsync(
            Path.Combine(_logDirectory, $"audit-{DateTime.UtcNow:yyyy-MM-dd}.ndjson"),
            [EventLine("Workflow.Created", DateTime.UtcNow), EventLine("User.Login", DateTime.UtcNow)],
            Ct);

        var result = await _reader.QueryAsync(new AuditQueryParameters { EventType = "User.Login", Limit = 10 }, Ct);

        Assert.Equal(1, result.Total);
        Assert.Equal("User.Login", result.Events[0].RootElement.GetProperty("eventType").GetString());
    }

    [Fact]
    public async Task QueryAsync_ByResourceType_Filters()
    {
        await File.WriteAllLinesAsync(
            Path.Combine(_logDirectory, $"audit-{DateTime.UtcNow:yyyy-MM-dd}.ndjson"),
            [EventLine("A", DateTime.UtcNow, "Workflow"), EventLine("B", DateTime.UtcNow, "User")],
            Ct);

        var result = await _reader.QueryAsync(new AuditQueryParameters { ResourceType = "User", Limit = 10 }, Ct);

        Assert.Equal(1, result.Total);
        Assert.Equal("User", result.Events[0].RootElement.GetProperty("resourceType").GetString());
    }

    [Fact]
    public async Task QueryAsync_ByResourceId_Filters()
    {
        var resourceId = Guid.NewGuid();
        await File.WriteAllLinesAsync(
            Path.Combine(_logDirectory, $"audit-{DateTime.UtcNow:yyyy-MM-dd}.ndjson"),
            [EventLine("A", DateTime.UtcNow, resourceId: resourceId), EventLine("B", DateTime.UtcNow, resourceId: Guid.NewGuid())],
            Ct);

        var result = await _reader.QueryAsync(new AuditQueryParameters { ResourceId = resourceId, Limit = 10 }, Ct);

        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task QueryAsync_ByDateRange_Filters()
    {
        var now = DateTime.UtcNow;
        await File.WriteAllLinesAsync(
            Path.Combine(_logDirectory, $"audit-{now:yyyy-MM-dd}.ndjson"),
            [EventLine("Old", now.AddDays(-2)), EventLine("New", now)],
            Ct);

        var result = await _reader.QueryAsync(new AuditQueryParameters { From = now.AddDays(-1), Limit = 10 }, Ct);

        Assert.Equal(1, result.Total);
        Assert.Equal("New", result.Events[0].RootElement.GetProperty("eventType").GetString());
    }

    [Fact]
    public async Task QueryAsync_MalformedLine_IsIgnored()
    {
        await File.WriteAllLinesAsync(
            Path.Combine(_logDirectory, $"audit-{DateTime.UtcNow:yyyy-MM-dd}.ndjson"),
            [EventLine("Valid", DateTime.UtcNow), "{ not json"],
            Ct);

        var result = await _reader.QueryAsync(new AuditQueryParameters { Limit = 10 }, Ct);

        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task QueryAsync_EmptyLine_IsIgnored()
    {
        await File.WriteAllLinesAsync(
            Path.Combine(_logDirectory, $"audit-{DateTime.UtcNow:yyyy-MM-dd}.ndjson"),
            [EventLine("Valid", DateTime.UtcNow), "", "   "],
            Ct);

        var result = await _reader.QueryAsync(new AuditQueryParameters { Limit = 10 }, Ct);

        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task QueryAsync_WithOffsetAndLimit_AppliesPaging()
    {
        var now = DateTime.UtcNow;
        await File.WriteAllLinesAsync(
            Path.Combine(_logDirectory, $"audit-{now:yyyy-MM-dd}.ndjson"),
            [EventLine("First", now.AddMinutes(-2)), EventLine("Second", now.AddMinutes(-1)), EventLine("Third", now)],
            Ct);

        var result = await _reader.QueryAsync(new AuditQueryParameters { Offset = 1, Limit = 1 }, Ct);

        Assert.Equal(3, result.Total);
        Assert.Single(result.Events);
        Assert.Equal("Second", result.Events[0].RootElement.GetProperty("eventType").GetString());
    }

    [Fact]
    public async Task QueryAsync_FilterByDateFileName_IgnoresOutOfRangeFiles()
    {
        var now = DateTime.UtcNow;
        await File.WriteAllLinesAsync(
            Path.Combine(_logDirectory, $"audit-{now.AddDays(-5):yyyy-MM-dd}.ndjson"),
            [EventLine("Old", now.AddDays(-5))],
            Ct);
        await File.WriteAllLinesAsync(
            Path.Combine(_logDirectory, $"audit-{now:yyyy-MM-dd}.ndjson"),
            [EventLine("New", now)],
            Ct);

        var result = await _reader.QueryAsync(new AuditQueryParameters { From = now.AddDays(-1), To = now.AddDays(1), Limit = 10 }, Ct);

        Assert.Equal(1, result.Total);
        Assert.Equal("New", result.Events[0].RootElement.GetProperty("eventType").GetString());
    }
}
