using FlowEngine.Infrastructure.Audit;
using Xunit;

namespace FlowEngine.Infrastructure.Tests.Audit;

public sealed class FlowEngineAuditEventTests
{
    [Fact]
    public void Properties_CanBeSetAndRetrieved()
    {
        var id = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var timestamp = DateTime.UtcNow;
        var payload = new Dictionary<string, object> { ["key"] = "value" };
        var metadata = new Dictionary<string, string> { ["ip"] = "127.0.0.1" };

        var e = new FlowEngineAuditEvent
        {
            Id = id,
            EventType = "Workflow.Created",
            Timestamp = timestamp,
            Actor = "user@example.com",
            ResourceType = "Workflow",
            ResourceId = resourceId,
            Payload = payload,
            Metadata = metadata,
        };

        Assert.Equal(id, e.Id);
        Assert.Equal("Workflow.Created", e.EventType);
        Assert.Equal(timestamp, e.Timestamp);
        Assert.Equal("user@example.com", e.Actor);
        Assert.Equal("Workflow", e.ResourceType);
        Assert.Equal(resourceId, e.ResourceId);
        Assert.Equal(payload, e.Payload);
        Assert.Equal(metadata, e.Metadata);
    }
}
