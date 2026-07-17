using System.Text.Json;
using FlowEngine.Application.Audit;
using FlowEngine.Core.Events;
using FlowEngine.Infrastructure.Audit;

namespace FlowEngine.Application.Tests.Audit;

/// <summary>
/// 审计日志写入 → 读取的往返测试（任务 2.2：Audit.NET 替换手搓序列化）。
/// 验证经 <see cref="AuditLogFileSink"/>（Audit.NET 序列化）落盘后，
/// <see cref="IAuditLogReader"/> 仍能按原 NDJSON 字段布局读回相同数据。
/// </summary>
public sealed class AuditLogFileSinkRoundTripTests : IDisposable
{
    private readonly string _logDir;

    public AuditLogFileSinkRoundTripTests()
    {
        _logDir = Path.Combine(Path.GetTempPath(), "flowengine-audit-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_logDir);
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.GetFiles(_logDir, "audit-*.ndjson"))
            {
                File.Delete(file);
            }

            Directory.Delete(_logDir, false);
        }
        catch
        {
            // 测试目录清理失败不影响结论。
        }
    }

    [Fact]
    public async Task Write_Then_Read_RoundTrips_AuditEvent_Fields()
    {
        var ct = TestContext.Current.CancellationToken;
        var eventId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

        // 使用关键事件类型（WriteEventAsync 内同步刷盘），避免依赖后台定时刷盘/关闭竞态，使断言确定性。
        var auditEvent = new AuditLogEvent
        {
            EventId = eventId,
            EventType = AuditEventTypes.CredentialDeleted,
            OccurredAt = occurredAt,
            Actor = "user-1",
            ResourceType = "Credential",
            ResourceId = resourceId,
            Payload = new Dictionary<string, object> { ["name"] = "Test", ["count"] = 3 },
            Metadata = new Dictionary<string, string> { ["ip"] = "127.0.0.1" },
        };

        var sink = new AuditLogFileSink(_logDir);
        try
        {
            await sink.OnEventAsync(auditEvent, ct);
            // 仅轮询目录列表（不受文件句柄共享冲突影响）确认后台处理器已创建文件。
            await WaitUntilFileCreatedAsync(ct);
            // 给后台处理器完成 WriteLine + 同步刷盘（关键事件）的余量。
            await Task.Delay(100, ct);
        }
        finally
        {
            // 释放文件句柄后再读取：reader 与长期持有写入句柄的 sink 存在既有 FileShare 限制。
            sink.Dispose();
        }

        IAuditLogReader reader = new AuditLogReader(_logDir);
        var result = await reader.QueryAsync(new AuditQueryParameters { Limit = 10 }, ct);

        var doc = Assert.Single(result.Events);
        Assert.Equal(1, result.Total);

        var root = doc.RootElement;
        Assert.Equal(eventId.ToString(), root.GetProperty("id").GetString());
        Assert.Equal(AuditEventTypes.CredentialDeleted, root.GetProperty("eventType").GetString());
        Assert.Equal("user-1", root.GetProperty("actor").GetString());
        Assert.Equal("Credential", root.GetProperty("resourceType").GetString());
        Assert.Equal(resourceId, root.GetProperty("resourceId").GetGuid());
        Assert.Equal(occurredAt, root.GetProperty("timestamp").GetDateTime());
        Assert.Equal("Test", root.GetProperty("payload").GetProperty("name").GetString());
        Assert.Equal(3, root.GetProperty("payload").GetProperty("count").GetInt32());
        Assert.Equal("127.0.0.1", root.GetProperty("metadata").GetProperty("ip").GetString());
    }

    [Fact]
    public async Task Write_Then_Read_Preserves_OnDisk_Ndjson_Format()
    {
        var ct = TestContext.Current.CancellationToken;

        var auditEvent = new AuditLogEvent
        {
            EventType = AuditEventTypes.CredentialDeleted,
            Actor = "svc",
            ResourceType = "Credential",
            ResourceId = Guid.NewGuid(),
            Payload = new Dictionary<string, object> { ["credentialId"] = Guid.NewGuid().ToString() },
        };

        var sink = new AuditLogFileSink(_logDir);
        try
        {
            await sink.OnEventAsync(auditEvent, ct);
            await WaitUntilFileCreatedAsync(ct);
            await Task.Delay(100, ct);
        }
        finally
        {
            sink.Dispose();
        }

        // 直接校验 on-disk 文件为单事件一行的 NDJSON，且字段名与历史格式一致
        // （reader 依赖 eventType / timestamp / resourceType / resourceId 等键）。
        IAuditLogReader reader = new AuditLogReader(_logDir);
        var result = await reader.QueryAsync(new AuditQueryParameters { Limit = 10 }, ct);
        Assert.Equal(1, result.Total);

        var file = Directory.GetFiles(_logDir, "audit-*.ndjson").Single();
        var line = (await File.ReadAllLinesAsync(file, ct)).Single();
        using var parsed = JsonDocument.Parse(line);
        var root = parsed.RootElement;
        Assert.True(root.TryGetProperty("eventType", out _));
        Assert.True(root.TryGetProperty("timestamp", out _));
        Assert.True(root.TryGetProperty("resourceId", out _));
        Assert.True(root.TryGetProperty("payload", out _));
    }

    private async Task WaitUntilFileCreatedAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(2000);
        while (DateTime.UtcNow < deadline)
        {
            if (Directory.Exists(_logDir) && Directory.GetFiles(_logDir, "audit-*.ndjson").Length == 1)
            {
                return;
            }

            await Task.Delay(25, ct).ConfigureAwait(false);
        }

        Assert.Fail("审计事件在超时内未落盘。");
    }
}
