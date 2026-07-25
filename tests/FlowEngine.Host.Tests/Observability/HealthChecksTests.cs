using FlowEngine.Core.Data;
using FlowEngine.Host.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace FlowEngine.Host.Tests.Observability;

/// <summary>
/// 健康检查（O-6）测试：liveness 探针不依赖数据库；readiness 探针包含数据库连通性探测，
/// 两者通过 Tag 区分，分别由 /health 与 /health/ready 端点暴露。
/// </summary>
public class HealthChecksTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<FlowEngineDbContext>(o => o.UseInMemoryDatabase("healthcheck-test"));
        services.AddFlowEngineHealthChecks();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task HealthChecks_RegisterLivenessAndDatabaseProbes()
    {
        using var provider = BuildProvider();
        var healthService = provider.GetRequiredService<HealthCheckService>();

        var report = await healthService.CheckHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Contains("liveness", report.Entries.Keys);
        Assert.Contains("database", report.Entries.Keys);
    }

    [Fact]
    public async Task Probes_AreTaggedForEndpointSeparation()
    {
        using var provider = BuildProvider();
        var healthService = provider.GetRequiredService<HealthCheckService>();

        var report = await healthService.CheckHealthAsync(TestContext.Current.CancellationToken);

        // liveness 探针仅含 "live" 标签，readiness 探针（数据库）仅含 "ready" 标签（O-6）。
        Assert.Contains(ObservabilityExtensions.LivenessTag, report.Entries["liveness"].Tags);
        Assert.Contains(ObservabilityExtensions.ReadinessTag, report.Entries["database"].Tags);
        Assert.DoesNotContain(ObservabilityExtensions.ReadinessTag, report.Entries["liveness"].Tags);
        Assert.DoesNotContain(ObservabilityExtensions.LivenessTag, report.Entries["database"].Tags);
    }
}
