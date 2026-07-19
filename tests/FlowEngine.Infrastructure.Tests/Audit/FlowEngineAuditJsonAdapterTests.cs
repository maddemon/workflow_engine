using System.Text;
using FlowEngine.Infrastructure.Audit;
using Xunit;

namespace FlowEngine.Infrastructure.Tests.Audit;

public sealed class FlowEngineAuditJsonAdapterTests
{
    private readonly FlowEngineAuditJsonAdapter _adapter = new();

    [Fact]
    public void Serialize_FlowEngineAuditEvent_ProducesCamelCaseFields()
    {
        var e = new FlowEngineAuditEvent
        {
            Id = Guid.NewGuid(),
            EventType = "Workflow.Created",
            Timestamp = new DateTime(2026, 7, 19, 0, 0, 0, DateTimeKind.Utc),
            Actor = "actor",
            ResourceType = "Workflow",
            ResourceId = Guid.NewGuid(),
            Payload = new Dictionary<string, object> { ["k"] = "v" },
            Metadata = new Dictionary<string, string> { ["ip"] = "127.0.0.1" },
        };

        var json = _adapter.Serialize(e);

        Assert.Contains("\"eventType\":\"Workflow.Created\"", json);
        Assert.Contains("\"resourceType\":\"Workflow\"", json);
        Assert.Contains("\"actor\":\"actor\"", json);
    }

    [Fact]
    public void Serialize_NonFlowEngineAuditEvent_FallsBackToStandardSerialization()
    {
        var anonymous = new { Name = "test", Value = 42 };

        var json = _adapter.Serialize(anonymous);

        Assert.Contains("\"name\":\"test\"", json);
        Assert.Contains("\"value\":42", json);
    }

    [Fact]
    public void Deserialize_ToFlowEngineAuditEvent_Roundtrip()
    {
        var e = new FlowEngineAuditEvent
        {
            Id = Guid.NewGuid(),
            EventType = "User.Login",
            ResourceType = "User",
        };

        var json = _adapter.Serialize(e);
        var deserialized = _adapter.Deserialize<FlowEngineAuditEvent>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(e.Id, deserialized.Id);
        Assert.Equal(e.EventType, deserialized.EventType);
        Assert.Equal(e.ResourceType, deserialized.ResourceType);
    }

    [Fact]
    public async Task SerializeAsync_FlowEngineAuditEvent_WritesToStream()
    {
        var e = new FlowEngineAuditEvent { EventType = "Test" };
        using var stream = new MemoryStream();

        await _adapter.SerializeAsync(stream, e, TestContext.Current.CancellationToken);

        var json = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("\"eventType\":\"Test\"", json);
    }

    [Fact]
    public async Task DeserializeAsync_FromStream_Roundtrip()
    {
        var e = new FlowEngineAuditEvent
        {
            Id = Guid.NewGuid(),
            EventType = "Test.Async",
        };
        var json = _adapter.Serialize(e);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var deserialized = await _adapter.DeserializeAsync<FlowEngineAuditEvent>(stream, TestContext.Current.CancellationToken);

        Assert.NotNull(deserialized);
        Assert.Equal(e.Id, deserialized.Id);
        Assert.Equal(e.EventType, deserialized.EventType);
    }

    [Fact]
    public void ToObject_TypedValue_ReturnsSameInstance()
    {
        var e = new FlowEngineAuditEvent { EventType = "Test" };

        var result = _adapter.ToObject<FlowEngineAuditEvent>(e);

        Assert.Same(e, result);
    }

    [Fact]
    public void ToObject_UntypedValue_Deserializes()
    {
        var e = new FlowEngineAuditEvent { EventType = "Test" };

        var result = _adapter.ToObject<FlowEngineAuditEvent>((object)e);

        Assert.NotNull(result);
        Assert.Equal(e.EventType, result.EventType);
    }

    [Fact]
    public void Deserialize_Untyped_ReturnsObject()
    {
        var e = new FlowEngineAuditEvent { EventType = "Test" };
        var json = _adapter.Serialize(e);

        var result = _adapter.Deserialize(json, typeof(FlowEngineAuditEvent));

        Assert.IsType<FlowEngineAuditEvent>(result);
    }
}
