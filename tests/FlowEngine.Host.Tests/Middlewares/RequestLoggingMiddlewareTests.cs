using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FlowEngine.Host.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FlowEngine.Host.Tests.Middlewares;

/// <summary>
/// RequestLoggingMiddleware（E-4）测试：记录非健康检查请求的方法/路径/状态码/耗时，
/// 排除 /health 与 /health/ready，且绝不记录查询字符串或负载。
/// </summary>
public class RequestLoggingMiddlewareTests
{
    private sealed class CaptureLogger : ILogger<RequestLoggingMiddleware>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        void ILogger.Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private static HttpContext CreateContext(string method, string path, int statusCode = 200)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.StatusCode = statusCode;
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task NormalRequest_LogsMethodPathStatus()
    {
        var logger = new CaptureLogger();
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new RequestLoggingMiddleware(next, logger);
        var context = CreateContext("POST", "/api/v1/workflows", 201);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("HTTP", entry.Message);
        Assert.Contains("POST", entry.Message);
        Assert.Contains("/api/v1/workflows", entry.Message);
        Assert.Contains("201", entry.Message);
        Assert.Contains("耗时", entry.Message);
    }

    [Fact]
    public async Task HealthRequest_IsSkipped()
    {
        var logger = new CaptureLogger();
        RequestDelegate next = _ => Task.CompletedTask;

        var middleware = new RequestLoggingMiddleware(next, logger);
        var context = CreateContext("GET", "/health");

        await middleware.InvokeAsync(context);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task HealthReadyRequest_IsSkipped()
    {
        var logger = new CaptureLogger();
        RequestDelegate next = _ => Task.CompletedTask;

        var middleware = new RequestLoggingMiddleware(next, logger);
        var context = CreateContext("GET", "/health/ready");

        await middleware.InvokeAsync(context);

        Assert.Empty(logger.Entries);
    }
}
