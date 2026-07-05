using System.Security.Claims;
using System.Text.Json;
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

public class RateLimitMiddlewareTests
{
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly Mock<ILogger<RateLimitMiddleware>> _logger = new();
    private readonly Mock<IEventBus> _eventBus;
    private readonly AuditEventFactory _auditFactory;
    private readonly IServiceProvider _serviceProvider;

    public RateLimitMiddlewareTests()
    {
        _eventBus = new Mock<IEventBus>();
        _auditFactory = new AuditEventFactory(new FakeUserContext { UserId = Guid.NewGuid() });
        _serviceProvider = new ServiceCollection()
            .AddSingleton<IEventBus>(_eventBus.Object)
            .AddScoped<AuditEventFactory>(_ => _auditFactory)
            .BuildServiceProvider();
    }

    private static readonly RateLimitOptions DefaultOptions = new()
    {
        Login = new RateLimitRule { PermitLimit = 2, WindowSeconds = 60, Enabled = true },
        Register = new RateLimitRule { PermitLimit = 3, WindowSeconds = 60, Enabled = true },
        Api = new RateLimitRule { PermitLimit = 5, WindowSeconds = 60, Enabled = true },
        WhitelistedPaths = ["/health", "/health/ready"],
    };

    [Fact]
    public async Task WithinLimit_CallsNext()
    {
        var context = CreateHttpContext("/api/v1/test");
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = CreateMiddleware(next);

        await middleware.InvokeAsync(context, next);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task ExceedsLimit_Returns429()
    {
        var context = CreateHttpContext("/api/v1/test");
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        // 5 requests allowed by default Api rule, 6th should be blocked
        for (var i = 0; i < 5; i++)
        {
            await middleware.InvokeAsync(CreateHttpContext("/api/v1/test"), next);
        }

        await middleware.InvokeAsync(context, next);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.NotNull(context.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task ExceedsLimit_Returns429WithJsonBody()
    {
        var context = CreateHttpContext("/api/v1/test");
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        for (var i = 0; i < 5; i++)
        {
            await middleware.InvokeAsync(CreateHttpContext("/api/v1/test"), next);
        }

        await middleware.InvokeAsync(context, next);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await JsonSerializer.DeserializeAsync<Dictionary<string, JsonElement>>(
            context.Response.Body,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal("TooManyRequests", body!["error"].GetString());
        Assert.True(body["retryAfter"].GetInt32() > 0);
    }

    [Fact]
    public async Task DifferentIps_AreIndependent()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        // Exhaust limit for IP 1
        for (var i = 0; i < 5; i++)
        {
            await middleware.InvokeAsync(CreateHttpContext("/api/v1/test", "192.168.1.1"), next);
        }

        // IP 1 should be blocked
        var context1 = CreateHttpContext("/api/v1/test", "192.168.1.1");
        await middleware.InvokeAsync(context1, next);
        Assert.Equal(StatusCodes.Status429TooManyRequests, context1.Response.StatusCode);

        // IP 2 should still pass
        var context2 = CreateHttpContext("/api/v1/test", "192.168.1.2");
        var nextCalled = false;
        RequestDelegate next2 = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware2 = CreateMiddleware(next2);
        await middleware2.InvokeAsync(context2, next2);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task DifferentUsers_AreIndependent()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        // Exhaust limit for user 1
        for (var i = 0; i < 5; i++)
        {
            await middleware.InvokeAsync(CreateHttpContext("/api/v1/test", userId: "user-1"), next);
        }

        // User 1 should be blocked
        var context1 = CreateHttpContext("/api/v1/test", userId: "user-1");
        await middleware.InvokeAsync(context1, next);
        Assert.Equal(StatusCodes.Status429TooManyRequests, context1.Response.StatusCode);

        // User 2 should still pass
        var context2 = CreateHttpContext("/api/v1/test", userId: "user-2");
        var nextCalled = false;
        RequestDelegate next2 = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware2 = CreateMiddleware(next2);
        await middleware2.InvokeAsync(context2, next2);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task WhitelistedPaths_BypassRateLimit()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        // Send 10 requests to /health (whitelisted)
        for (var i = 0; i < 10; i++)
        {
            var context = CreateHttpContext("/health");
            await middleware.InvokeAsync(context, next);
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }
    }

    [Fact]
    public async Task LoginPath_UsesLoginRule()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        // Login allows 2 requests
        await middleware.InvokeAsync(CreateHttpContext("/auth/login"), next);
        await middleware.InvokeAsync(CreateHttpContext("/auth/login"), next);

        // 3rd should be blocked
        var blocked = CreateHttpContext("/auth/login");
        await middleware.InvokeAsync(blocked, next);

        Assert.Equal(StatusCodes.Status429TooManyRequests, blocked.Response.StatusCode);
    }

    [Fact]
    public async Task RegisterPath_UsesRegisterRule()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        // Register allows 3 requests
        for (var i = 0; i < 3; i++)
        {
            await middleware.InvokeAsync(CreateHttpContext("/auth/register"), next);
        }

        // 4th should be blocked
        var blocked = CreateHttpContext("/auth/register");
        await middleware.InvokeAsync(blocked, next);

        Assert.Equal(StatusCodes.Status429TooManyRequests, blocked.Response.StatusCode);
    }

    [Fact]
    public async Task DisabledRule_PassesThrough()
    {
        var options = new RateLimitOptions
        {
            Api = new RateLimitRule { PermitLimit = 1, WindowSeconds = 60, Enabled = false },
            WhitelistedPaths = [],
        };
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next, options);

        // Even though PermitLimit is 1, rule is disabled so all pass
        for (var i = 0; i < 10; i++)
        {
            var context = CreateHttpContext("/api/v1/test");
            await middleware.InvokeAsync(context, next);
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }
    }

    [Fact]
    public async Task RetryAfterHeader_IsPresent()
    {
        var context = CreateHttpContext("/api/v1/test");
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = CreateMiddleware(next);

        for (var i = 0; i < 5; i++)
        {
            await middleware.InvokeAsync(CreateHttpContext("/api/v1/test"), next);
        }

        await middleware.InvokeAsync(context, next);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.False(string.IsNullOrEmpty(context.Response.Headers.RetryAfter.ToString()));
    }

    private HttpContext CreateHttpContext(
        string path,
        string? ip = null,
        string? userId = null)
    {
        var context = new DefaultHttpContext
        {
            Request = { Path = path },
            Response = { Body = new MemoryStream() },
            RequestServices = _serviceProvider,
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

    private RateLimitMiddleware CreateMiddleware(
        RequestDelegate next,
        RateLimitOptions? options = null)
    {
        return new RateLimitMiddleware(
            _cache,
            Options.Create(options ?? DefaultOptions),
            _logger.Object,
            _eventBus.Object,
            _auditFactory);
    }

    private sealed class FakeUserContext : IUserContext
    {
        public bool IsAuthenticated => UserId.HasValue;
        public Guid? UserId { get; set; }
        public string? Email => "test@test.com";
        public IReadOnlyList<string> Roles { get; set; } = [];
    }
}
