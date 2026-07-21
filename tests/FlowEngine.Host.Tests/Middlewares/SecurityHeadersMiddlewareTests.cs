using FlowEngine.Host.Middlewares;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;

namespace FlowEngine.Host.Tests.Middlewares;

public class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AddsExpectedSecurityHeaders()
    {
        var environment = new MockWebHostEnvironment { EnvironmentName = "Production" };
        var context = new DefaultHttpContext();
        var middleware = new SecurityHeadersMiddleware(
            _ => Task.CompletedTask,
            environment);

        await middleware.InvokeAsync(context);

        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions);
        Assert.Equal("DENY", context.Response.Headers.XFrameOptions);
        Assert.Equal("1; mode=block", context.Response.Headers.XXSSProtection);
        Assert.Equal("strict-origin-when-cross-origin", context.Response.Headers["Referrer-Policy"]);
        Assert.Equal("default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'", context.Response.Headers["Content-Security-Policy"]);
        Assert.Equal("max-age=31536000; includeSubDomains", context.Response.Headers["Strict-Transport-Security"]);
    }

    [Fact]
    public async Task InvokeAsync_DevelopmentEnvironment_UsesRelaxedCsp()
    {
        var environment = new MockWebHostEnvironment { EnvironmentName = "Development" };
        var context = new DefaultHttpContext();
        var middleware = new SecurityHeadersMiddleware(
            _ => Task.CompletedTask,
            environment);

        await middleware.InvokeAsync(context);

        Assert.Equal("default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; connect-src 'self' ws: wss:", context.Response.Headers["Content-Security-Policy"]);
        // 开发环境不应下发 HSTS，避免浏览器 HSTS 缓存导致本地 HTTP 调试困难。
        Assert.False(context.Response.Headers.ContainsKey("Strict-Transport-Security"));
    }

    [Fact]
    public async Task InvokeAsync_CallsNextMiddleware()
    {
        var environment = new MockWebHostEnvironment { EnvironmentName = "Production" };
        var context = new DefaultHttpContext();
        var nextCalled = false;
        var middleware = new SecurityHeadersMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            environment);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    private sealed class MockWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "FlowEngine.Host.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Production";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = string.Empty;
    }
}
