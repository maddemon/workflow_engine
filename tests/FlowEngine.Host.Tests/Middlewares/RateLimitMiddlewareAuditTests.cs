using System.Security.Claims;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Identity;
using FlowEngine.Application.RateLimiting;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Events;
using FlowEngine.Host.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace FlowEngine.Host.Tests.Middlewares;

public class RateLimitMiddlewareAuditTests
{
    private static readonly RateLimitOptions DefaultOptions = new()
    {
        Login = new RateLimitRule { PermitLimit = 2, WindowSeconds = 60, Enabled = true },
        Register = new RateLimitRule { PermitLimit = 3, WindowSeconds = 60, Enabled = true },
        Api = new RateLimitRule { PermitLimit = 5, WindowSeconds = 60, Enabled = true },
        WhitelistedPaths = [],
    };

    [Fact]
    public async Task ExceedsLimit_PublishesRateLimitedAuditEvent()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new Mock<ILogger<RateLimitMiddleware>>();
        var eventBus = new Mock<IEventBus>();
        var auditFactory = new AuditEventFactory(new FakeUserContext { UserId = Guid.NewGuid() });
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IEventBus>(eventBus.Object)
            .AddScoped<AuditEventFactory>(_ => auditFactory)
            .BuildServiceProvider();
        var middleware = new RateLimitMiddleware(
            _ => Task.CompletedTask,
            cache,
            Options.Create(DefaultOptions),
            logger.Object);

        for (var i = 0; i < 5; i++)
        {
            var warmupContext = CreateHttpContext("/api/v1/test", ip: "192.168.1.1");
            warmupContext.RequestServices = serviceProvider;
            await middleware.InvokeAsync(warmupContext);
        }

        var context = CreateHttpContext("/api/v1/test", ip: "192.168.1.1");
        context.RequestServices = serviceProvider;
        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        eventBus.Verify(
            x => x.PublishAsync(
                It.Is<AuditLogEvent>(
                    e => e.EventType == AuditEventTypes.RateLimited
                         && e.ResourceType == "Security"
                         && e.ResourceId == Guid.Empty
                         && e.Payload != null
                         && e.Payload.ContainsKey("identifier")
                         && e.Payload["identifier"].ToString()!.Contains("192.168.1.1")
                         && e.Payload.ContainsKey("rule")
                         && e.Payload["rule"].ToString() == "Api"),
                CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task WithinLimit_DoesNotPublishRateLimitedEvent()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new Mock<ILogger<RateLimitMiddleware>>();
        var eventBus = new Mock<IEventBus>();
        var auditFactory = new AuditEventFactory(new FakeUserContext { UserId = Guid.NewGuid() });
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IEventBus>(eventBus.Object)
            .AddScoped<AuditEventFactory>(_ => auditFactory)
            .BuildServiceProvider();
        var middleware = new RateLimitMiddleware(
            _ => Task.CompletedTask,
            cache,
            Options.Create(DefaultOptions),
            logger.Object);

        var context = CreateHttpContext("/api/v1/test");
        context.RequestServices = serviceProvider;
        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        eventBus.Verify(
            x => x.PublishAsync(It.IsAny<AuditLogEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static HttpContext CreateHttpContext(string path, string? ip = null, string? userId = null)
    {
        var context = new DefaultHttpContext
        {
            Request = { Path = path },
            Response = { Body = new MemoryStream() },
        };

        if (ip is not null)
        {
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
        }

        if (userId is not null)
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId)],
                "test");
            context.User = new ClaimsPrincipal(identity);
        }

        return context;
    }

    private sealed class FakeUserContext : IUserContext
    {
        public bool IsAuthenticated => UserId.HasValue;
        public Guid? UserId { get; set; }
        public string? Email => "test@test.com";
        public IReadOnlyList<string> Roles { get; set; } = [];
    }
}
