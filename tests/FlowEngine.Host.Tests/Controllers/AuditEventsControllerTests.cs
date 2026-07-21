using System.Text.Json;
using FlowEngine.Application.Audit;
using FlowEngine.Host.Controllers;

namespace FlowEngine.Host.Tests.Controllers;

/// <summary>
/// 审计事件查询端点测试：验证 <see cref="AuditEventsController.Query"/> 将审计文档
/// 转换为 JSON 节点树后，输出结构（字段名/类型/嵌套）与原始 JSON 完全一致。
/// </summary>
public sealed class AuditEventsControllerTests
{
    [Fact]
    public async Task Query_ReturnsEvents_WithOriginalJsonStructure()
    {
        var json = """
                   {
                     "id": "evt-1",
                     "type": "FileAccessed",
                     "timestamp": "2026-07-20T10:00:00Z",
                     "detail": { "path": "/a/b", "count": 3 }
                   }
                   """;
        using var doc = JsonDocument.Parse(json);
        var reader = new FakeAuditLogReader([doc]);
        var controller = new AuditEventsController(reader);

        var result = await controller.Query(new AuditQueryParameters { Offset = 0, Limit = 50 }, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        using var parsed = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var root = parsed.RootElement;

        Assert.Equal(1, root.GetProperty("total").GetInt32());
        Assert.Equal(0, root.GetProperty("offset").GetInt32());
        Assert.Equal(50, root.GetProperty("limit").GetInt32());

        var events = root.GetProperty("events");
        Assert.Equal(JsonValueKind.Array, events.ValueKind);
        var first = events[0];
        Assert.Equal("evt-1", first.GetProperty("id").GetString());
        Assert.Equal("FileAccessed", first.GetProperty("type").GetString());
        Assert.Equal("2026-07-20T10:00:00Z", first.GetProperty("timestamp").GetString());
        var detail = first.GetProperty("detail");
        Assert.Equal("/a/b", detail.GetProperty("path").GetString());
        Assert.Equal(3, detail.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Query_MultipleEvents_PreservesEachStructure()
    {
        using var doc1 = JsonDocument.Parse("""{ "id": "a", "value": 1 }""");
        using var doc2 = JsonDocument.Parse("""{ "id": "b", "value": 2.5 }""");
        var reader = new FakeAuditLogReader([doc1, doc2]);
        var controller = new AuditEventsController(reader);

        var result = await controller.Query(new AuditQueryParameters(), TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        using var parsed = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var events = parsed.RootElement.GetProperty("events");

        Assert.Equal(2, events.GetArrayLength());
        Assert.Equal("a", events[0].GetProperty("id").GetString());
        Assert.Equal(1, events[0].GetProperty("value").GetInt32());
        Assert.Equal("b", events[1].GetProperty("id").GetString());
        Assert.Equal(2.5, events[1].GetProperty("value").GetDouble());
    }

    private sealed class FakeAuditLogReader : IAuditLogReader
    {
        private readonly IReadOnlyList<JsonDocument> _docs;

        public FakeAuditLogReader(IReadOnlyList<JsonDocument> docs) => _docs = docs;

        public Task<AuditQueryResult> QueryAsync(AuditQueryParameters parameters, CancellationToken cancellationToken = default)
            => Task.FromResult(new AuditQueryResult { Events = _docs, Total = _docs.Count });
    }
}
