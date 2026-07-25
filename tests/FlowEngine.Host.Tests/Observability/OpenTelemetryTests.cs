using FlowEngine.Host.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using System.Diagnostics;
using Xunit;

namespace FlowEngine.Host.Tests.Observability;

/// <summary>
/// OpenTelemetry 集成（O-2）测试：确认 ASP.NET Core 与 HttpClient 仪表已注册，
/// 且对目标 ActivitySource 产生了采样监听。
/// </summary>
public class OpenTelemetryTests
{
    [Fact]
    public void AspNetCoreInstrumentation_IsRegisteredAndSampled()
    {
        var services = new ServiceCollection();
        services.AddFlowEngineOpenTelemetry(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        // 解析 TracerProvider 触发 OpenTelemetry 提供方启动并注册对仪表源的监听。
        var tracerProvider = provider.GetRequiredService<TracerProvider>();

        var source = new ActivitySource("Microsoft.AspNetCore");
        // HasListeners 为 true 表明 OpenTelemetry 已为 ASP.NET Core 源注册监听（O-2）。
        Assert.True(source.HasListeners());

        using var activity = source.StartActivity("TestRequest");
        // 默认采样器（AlwaysOn）应对已注册源请求记录数据，证明分布式追踪链路可用。
        Assert.NotNull(activity);
        Assert.True(activity!.IsAllDataRequested);
    }

    [Fact]
    public void HttpClientInstrumentation_IsRegistered()
    {
        var services = new ServiceCollection();
        services.AddFlowEngineOpenTelemetry(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<TracerProvider>();

        var source = new ActivitySource("System.Net.Http");
        Assert.True(source.HasListeners());
    }
}
