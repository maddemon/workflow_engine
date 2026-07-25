using FlowEngine.Core.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace FlowEngine.Host.Observability;

/// <summary>
/// 可观测性基础设施注册（O-2 / O-6）：
/// - OpenTelemetry 分布式追踪与指标（ASP.NET Core + HttpClient 仪表，stdout 导出）。
/// - 健康检查（liveness 存活探针 + readiness 就绪探针含数据库探测）。
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>liveness 探针标签，由 /health 端点暴露。</summary>
    public const string LivenessTag = "live";

    /// <summary>readiness 探针标签，由 /health/ready 端点暴露，含数据库连通性探测。</summary>
    public const string ReadinessTag = "ready";

    /// <summary>
    /// 注册 OpenTelemetry 追踪与指标（O-2）。
    /// 资源名取自配置 <c>OpenTelemetry:ServiceName</c>（默认 FlowEngine），
    /// 仪器化 ASP.NET Core 与 HttpClient，并以 Console（stdout）导出。
    /// </summary>
    public static IServiceCollection AddFlowEngineOpenTelemetry(
        this IServiceCollection services, IConfiguration configuration)
    {
        var serviceName = configuration["OpenTelemetry:ServiceName"] ?? "FlowEngine";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddConsoleExporter())
            .WithMetrics(metrics => metrics
                .AddMeter(FlowEngine.Runtime.Diagnostics.FlowEngineMetrics.MeterName)
                .AddConsoleExporter());

        return services;
    }

    /// <summary>
    /// 注册健康检查（O-6）：liveness 探针（不依赖外部依赖，仅返回健康）与
    /// readiness 探针（含数据库连通性探测）。两者通过 Tag 区分，分别由
    /// <c>/health</c> 与 <c>/health/ready</c> 端点暴露。
    /// </summary>
    public static IServiceCollection AddFlowEngineHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck("liveness", () => HealthCheckResult.Healthy(), tags: [LivenessTag])
            .AddCheck<DatabaseHealthCheck>("database", tags: [ReadinessTag]);

        return services;
    }
}

/// <summary>
/// 数据库连通性健康检查（O-6 readiness 探针）：仅探测数据库是否可连接，不读取业务数据。
/// 由 <see cref="AddFlowEngineHealthChecks"/> 以 readiness 标签注册，供 /health/ready 端点使用。
/// </summary>
public sealed class DatabaseHealthCheck(FlowEngineDbContext dbContext) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
            return canConnect
                ? HealthCheckResult.Healthy("数据库可连接。")
                : HealthCheckResult.Unhealthy("数据库不可连接。");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("数据库探测失败。", ex);
        }
    }
}
