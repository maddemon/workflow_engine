using FlowEngine.Host.Options;
using FlowEngine.Host.Webhooks;

namespace FlowEngine.Host.Tests;

/// <summary>
/// Webhook 重放保护与按路由/IP 限流的单元测试（SEC-3）。
/// 两类组件均为无依赖的轻量内存实现，直接驱动 <c>TryAccept</c> / <c>TryAcquire</c> 即可验证行为。
/// </summary>
public class WebhookSecurityTests
{
    [Fact]
    public void ReplayCache_AcceptsFreshNonce_AndRejectsReplay()
    {
        var cache = new WebhookReplayCache(Microsoft.Extensions.Options.Options.Create(new WebhookSecurityOptions()));
        const string route = "/wh/a";
        const string nonce = "n1";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.True(cache.TryAccept(route, nonce, now, now, out var firstError));
        Assert.Null(firstError);

        // 同一 route+nonce 重复提交应被拒绝（重放）。
        Assert.False(cache.TryAccept(route, nonce, now, now, out var replayError));
        Assert.Equal("nonce already used (possible replay)", replayError);
    }

    [Fact]
    public void ReplayCache_RejectsExpiredTimestamp()
    {
        var options = new WebhookSecurityOptions { ReplayWindowSeconds = 300, MaxClockSkewSeconds = 30 };
        var cache = new WebhookReplayCache(Microsoft.Extensions.Options.Options.Create(options));
        var now = 1_000L;

        // 时间戳早于窗口（now - window - 1）视为过期。
        Assert.False(cache.TryAccept("/wh/a", "n1", now - options.ReplayWindowSeconds - 1, now, out var error));
        Assert.Equal("timestamp expired or outside allowed clock skew", error);
    }

    [Fact]
    public void ReplayCache_RejectsTimestampBeyondClockSkew()
    {
        var options = new WebhookSecurityOptions { ReplayWindowSeconds = 300, MaxClockSkewSeconds = 30 };
        var cache = new WebhookReplayCache(Microsoft.Extensions.Options.Options.Create(options));
        var now = 1_000L;

        // 时间戳晚于 now + MaxClockSkew 视为超出允许时钟偏差。
        Assert.False(cache.TryAccept("/wh/a", "n1", now + options.MaxClockSkewSeconds + 1, now, out var error));
        Assert.Equal("timestamp expired or outside allowed clock skew", error);
    }

    [Fact]
    public void ReplayCache_DifferentRoutes_AllowSameNonce()
    {
        var cache = new WebhookReplayCache(Microsoft.Extensions.Options.Options.Create(new WebhookSecurityOptions()));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.True(cache.TryAccept("/wh/a", "same-nonce", now, now, out _));
        // 不同路由路径视为不同命名空间，同一 nonce 可再次接受。
        Assert.True(cache.TryAccept("/wh/b", "same-nonce", now, now, out _));
    }

    [Fact]
    public void ReplayCache_DisabledByOption_AlwaysAccepts()
    {
        var cache = new WebhookReplayCache(Microsoft.Extensions.Options.Options.Create(new WebhookSecurityOptions { EnableReplayProtection = false }));

        Assert.True(cache.TryAccept("/wh/a", "n1", 0, 0, out _));
        Assert.True(cache.TryAccept("/wh/a", "n1", 0, 0, out _));
    }

    [Fact]
    public void RateLimiter_AllowsUpToPermitCount_ThenRejects()
    {
        var options = new WebhookSecurityOptions { RateLimitPermitCount = 2, RateLimitWindowSeconds = 60 };
        var limiter = new WebhookRateLimiter(Microsoft.Extensions.Options.Options.Create(options));
        const string route = "/wh/a";
        const string ip = "1.2.3.4";
        var now = 1_000L;

        Assert.True(limiter.TryAcquire(route, ip, now));
        Assert.True(limiter.TryAcquire(route, ip, now));
        // 超过窗口内允许的最大请求数后应被拒绝（返回 429 语义）。
        Assert.False(limiter.TryAcquire(route, ip, now));
    }

    [Fact]
    public void RateLimiter_DifferentKeys_AreIndependent()
    {
        var options = new WebhookSecurityOptions { RateLimitPermitCount = 1, RateLimitWindowSeconds = 60 };
        var limiter = new WebhookRateLimiter(Microsoft.Extensions.Options.Options.Create(options));
        var now = 1_000L;

        Assert.True(limiter.TryAcquire("/wh/a", "1.1.1.1", now));
        // 不同 IP 在各自窗口内独立计数，不受影响。
        Assert.True(limiter.TryAcquire("/wh/a", "9.9.9.9", now));
    }

    [Fact]
    public void RateLimiter_DisabledByOption_AlwaysAcquires()
    {
        var limiter = new WebhookRateLimiter(Microsoft.Extensions.Options.Options.Create(new WebhookSecurityOptions { EnableRateLimit = false }));

        for (var i = 0; i < 10; i++)
        {
            Assert.True(limiter.TryAcquire("/wh/a", "1.1.1.1", i));
        }
    }
}
