using FlowEngine.Host.Webhooks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FlowEngine.Host.Tests.Middlewares;

public class WebhookRoutingMiddlewareTests
{
    private readonly Mock<IWebhookHandler> _handler = new();
    private readonly Mock<ILogger<WebhookRoutingMiddleware>> _logger = new();

    public WebhookRoutingMiddlewareTests()
    {
        _handler
            .Setup(h => h.HandleAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
    }

    private HttpContext CreateContext(string path, string method = "POST")
    {
        var services = new ServiceCollection()
            .AddSingleton(_handler.Object)
            .BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = services,
            Response = { Body = new MemoryStream() },
        };
        context.Request.Path = path;
        context.Request.Method = method;
        return context;
    }

    private WebhookRoutingMiddleware CreateMiddleware(RequestDelegate next)
        => new(next, _logger.Object);

    [Fact]
    public async Task PostToWebhookPath_DispatchesToHandler_WithPath()
    {
        var context = CreateContext("/hooks/my-route");
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        _handler.Verify(h => h.HandleAsync(It.IsAny<HttpContext>(), "/hooks/my-route"), Times.Once);
    }

    [Fact]
    public async Task GetRequest_CallsNext_NotHandler()
    {
        var context = CreateContext("/hooks/my-route", "GET");
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        _handler.Verify(h => h.HandleAsync(It.IsAny<HttpContext>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PostToApiPath_CallsNext_NotHandler()
    {
        var context = CreateContext("/api/v1/workflows");
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        _handler.Verify(h => h.HandleAsync(It.IsAny<HttpContext>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PostToHealth_CallsNext_NotHandler()
    {
        var context = CreateContext("/health");
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        _handler.Verify(h => h.HandleAsync(It.IsAny<HttpContext>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PostToWebSocketPath_CallsNext_NotHandler()
    {
        var context = CreateContext("/ws/execution");
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        _handler.Verify(h => h.HandleAsync(It.IsAny<HttpContext>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EmptyPath_CallsNext_NotHandler()
    {
        var context = CreateContext(string.Empty);
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        _handler.Verify(h => h.HandleAsync(It.IsAny<HttpContext>(), It.IsAny<string>()), Times.Never);
    }
}
