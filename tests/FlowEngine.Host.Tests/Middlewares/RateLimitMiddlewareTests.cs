using System.Net;
using System.Text.Json;
using Xunit;

namespace FlowEngine.Host.Tests.Middlewares;

/// <summary>
/// 限流集成测试：基于内置 System.Threading.RateLimiting（全局分区限流器）验证
/// 允许阈值、429 响应、Retry-After、JSON 错误体、白名单、按路径分策略与禁用跳过等行为。
/// 每个测试使用独立的最小宿主（<see cref="RateLimitTestApp"/>），互不干扰。
/// </summary>
public class RateLimitMiddlewareTests
{
    [Fact]
    public async Task ExceedsApiLimit_Returns429_WithJsonBody_AndRetryAfter()
    {
        await using var app = RateLimitTestApp.Create();
        await app.StartAsync();
        var client = app.Client;
        var ct = TestContext.Current.CancellationToken;

        // Api 规则允许 5 次，前 5 次不应被限流。
        for (var i = 0; i < 5; i++)
        {
            var ok = await client.GetAsync("/api/v1/test", ct);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, ok.StatusCode);
        }

        // 第 6 次应被限流。
        var blocked = await client.GetAsync("/api/v1/test", ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("Retry-After"));

        var body = await blocked.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("RateLimited", doc.RootElement.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task WhitelistedPaths_BypassRateLimit()
    {
        await using var app = RateLimitTestApp.Create();
        await app.StartAsync();
        var client = app.Client;
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 20; i++)
        {
            var response = await client.GetAsync("/health", ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task LoginPath_UsesLoginRule()
    {
        await using var app = RateLimitTestApp.Create();
        await app.StartAsync();
        var client = app.Client;
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 2; i++)
        {
            var ok = await client.GetAsync("/auth/login", ct);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, ok.StatusCode);
        }

        var blocked = await client.GetAsync("/auth/login", ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task RegisterPath_UsesRegisterRule()
    {
        await using var app = RateLimitTestApp.Create();
        await app.StartAsync();
        var client = app.Client;
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 3; i++)
        {
            var ok = await client.GetAsync("/auth/register", ct);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, ok.StatusCode);
        }

        var blocked = await client.GetAsync("/auth/register", ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task DisabledApiRule_PassesThrough()
    {
        await using var app = RateLimitTestApp.Create(disableApi: true);
        await app.StartAsync();
        var client = app.Client;
        var ct = TestContext.Current.CancellationToken;

        // Api 规则被禁用时所有请求都不应被限流。
        for (var i = 0; i < 20; i++)
        {
            var response = await client.GetAsync("/api/v1/test", ct);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }
}
