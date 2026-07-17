using System.Security.Claims;
using FlowEngine.Application.RateLimiting;
using FlowEngine.Host.RateLimiting;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FlowEngine.Host.Tests.Middlewares;

/// <summary>
/// 限流公共逻辑单元测试：验证每客户端分区键计算、路径分类与白名单判定。
/// 这些逻辑直接复用于内置限流器的全局分区器，确保其与原手搓中间件行为一致。
/// </summary>
public class RateLimitKeyResolverTests
{
    private static HttpContext CreateContext(string? ip = null, string? userId = null)
    {
        var context = new DefaultHttpContext();
        if (ip is not null)
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
        if (userId is not null)
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test");
            context.User = new ClaimsPrincipal(identity);
        }

        return context;
    }

    [Fact]
    public void GetClientKey_AnonymousRequest_UsesIpAndPolicy()
    {
        var context = CreateContext(ip: "192.168.1.1");
        Assert.Equal("ip:192.168.1.1:Api", RateLimitKeyResolver.GetClientKey(context, "Api"));
    }

    [Fact]
    public void GetClientKey_AuthenticatedRequest_UsesUserIdAndPolicy()
    {
        var context = CreateContext(userId: "user-42");
        Assert.Equal("user:user-42:Login", RateLimitKeyResolver.GetClientKey(context, "Login"));
    }

    [Fact]
    public void GetClientKey_DifferentIps_ProduceDifferentKeys()
    {
        var a = RateLimitKeyResolver.GetClientKey(CreateContext(ip: "10.0.0.1"), "Api");
        var b = RateLimitKeyResolver.GetClientKey(CreateContext(ip: "10.0.0.2"), "Api");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void GetClientKey_DifferentUsers_ProduceDifferentKeys()
    {
        var a = RateLimitKeyResolver.GetClientKey(CreateContext(userId: "u1"), "Api");
        var b = RateLimitKeyResolver.GetClientKey(CreateContext(userId: "u2"), "Api");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ClassifyPath_MapsLoginRegisterAndApi()
    {
        var options = new RateLimitOptions();
        Assert.Equal("Login", RateLimitKeyResolver.ClassifyPath(options, "/auth/login").PolicyName);
        Assert.Equal("Register", RateLimitKeyResolver.ClassifyPath(options, "/account/register").PolicyName);
        Assert.Equal("Api", RateLimitKeyResolver.ClassifyPath(options, "/api/v1/workflows").PolicyName);
        Assert.Equal("Api", RateLimitKeyResolver.ClassifyPath(options, "/something/else").PolicyName);
    }

    [Fact]
    public void IsWhitelisted_MatchesCaseInsensitiveIgnoringTrailingSlash()
    {
        var options = new RateLimitOptions { WhitelistedPaths = ["/health", "/health/ready"] };
        Assert.True(RateLimitKeyResolver.IsWhitelisted(options, "/HEALTH/"));
        Assert.False(RateLimitKeyResolver.IsWhitelisted(options, "/api/v1/test"));
    }
}
