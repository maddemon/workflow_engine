using System.Net;
using FlowEngine.Application.Audit;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Events;
using Moq;
using Xunit;

namespace FlowEngine.Host.Tests.Middlewares;

/// <summary>
/// 限流审计集成测试：验证超限时通过 IEventBus 发布 RateLimited 审计事件，
/// 未超限时不发布。使用 <see cref="RateLimitTestApp"/> 注册 Moq 桩的 IEventBus。
/// </summary>
public class RateLimitMiddlewareAuditTests
{
    [Fact]
    public async Task ExceedsLimit_PublishesRateLimitedAuditEvent()
    {
        var eventBusMock = new Mock<IEventBus>();
        await using var app = RateLimitTestApp.Create(eventBusMock: eventBusMock);
        await app.StartAsync();
        var client = app.Client;
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 5; i++)
        {
            await client.GetAsync("/api/v1/test", ct);
        }

        var blocked = await client.GetAsync("/api/v1/test", ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);

        eventBusMock.Verify(
            x => x.PublishAsync(
                It.Is<AuditLogEvent>(e =>
                    e.EventType == AuditEventTypes.RateLimited
                    && e.ResourceType == "Security"
                    && e.Payload != null
                    && e.Payload.ContainsKey("identifier")
                    && e.Payload.ContainsKey("rule")
                    && e.Payload["rule"]!.ToString() == "Api"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task WithinLimit_DoesNotPublishRateLimitedEvent()
    {
        var eventBusMock = new Mock<IEventBus>();
        await using var app = RateLimitTestApp.Create(eventBusMock: eventBusMock);
        await app.StartAsync();
        var client = app.Client;
        var ct = TestContext.Current.CancellationToken;

        var response = await client.GetAsync("/api/v1/test", ct);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);

        eventBusMock.Verify(
            x => x.PublishAsync(It.IsAny<AuditLogEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
