using System;
using System.Net;
using System.Threading.Tasks;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Identity;
using FlowEngine.Application.RateLimiting;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Events;
using FlowEngine.Host.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace FlowEngine.Host.Tests.Middlewares;

/// <summary>
/// 自包含的最小 WebApplication 测试宿主：仅注册内置限流器（<see cref="RateLimiterSetup"/>）与若干终结点，
/// 不经过 Program / 数据库 / 迁移，每个实例独立且随测试释放，避免全应用集成测试共享宿主导致的并发释放问题。
/// </summary>
public sealed class RateLimitTestApp : IAsyncDisposable
{
    private readonly WebApplication _app;

    private RateLimitTestApp(WebApplication app) => _app = app;

    /// <summary>测试用 HttpClient（绑定到 TestServer）。</summary>
    public HttpClient Client => _app.GetTestClient();

    /// <summary>
    /// 创建一个已配置限流的最小宿主。
    /// </summary>
    /// <param name="disableApi">是否禁用 Api 规则（用于"禁用跳过"测试）。</param>
    /// <param name="eventBusMock">提供则注册为 IEventBus 并启用审计工厂，用于验证超限审计事件。</param>
    public static RateLimitTestApp Create(bool disableApi = false, Mock<IEventBus>? eventBusMock = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        // 注册内置全局分区限流器（按路径分类 + 每客户端独立限流），与宿主保持一致。
        builder.Services.AddRateLimiter(RateLimiterSetup.Configure);
        builder.Services.Configure<RateLimitOptions>(o =>
        {
            o.Login = new RateLimitRule { PermitLimit = 2, WindowSeconds = 3600, Enabled = true };
            o.Register = new RateLimitRule { PermitLimit = 3, WindowSeconds = 3600, Enabled = true };
            o.Api = new RateLimitRule { PermitLimit = 5, WindowSeconds = 3600, Enabled = !disableApi };
            o.WhitelistedPaths = ["/health", "/health/ready"];
        });

        if (eventBusMock is not null)
        {
            builder.Services.AddSingleton<IEventBus>(eventBusMock.Object);
            builder.Services.AddScoped<AuditEventFactory>(_ => new AuditEventFactory(new FakeUserContext()));
        }

        var app = builder.Build();
        app.UseRateLimiter();
        app.MapGet("/api/v1/test", () => Results.Ok());
        app.MapGet("/auth/login", () => Results.Ok());
        app.MapGet("/auth/register", () => Results.Ok());
        app.MapGet("/health", () => Results.Ok());

        return new RateLimitTestApp(app);
    }

    public Task StartAsync() => _app.StartAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();
}

/// <summary>
/// 匿名用户上下文桩，供审计事件工厂在测试中使用。
/// </summary>
file sealed class FakeUserContext : IUserContext
{
    public bool IsAuthenticated => false;
    public Guid? UserId => null;
    public string? Email => "test@test.com";
    public IReadOnlyList<string> Roles => [];
}
