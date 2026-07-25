using FlowEngine.Host;
using FlowEngine.Host.Observability;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// O-1：以 Serilog 作为日志提供方，配置来自 appsettings（保留 Console 接收端）。
builder.Host.UseSerilog((ctx, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration);
    // 始终保留 Console 接收端，确保日志不丢失（O-1）。
    cfg.WriteTo.Console();
});

builder.Services.AddFlowEngine(builder.Configuration, builder.Environment);

// O-2：OpenTelemetry 分布式追踪与指标（ASP.NET Core + HttpClient 仪表，stdout 导出）。
builder.Services.AddFlowEngineOpenTelemetry(builder.Configuration);

// O-6：健康检查（liveness 存活探针 + readiness 就绪探针含数据库探测）。
builder.Services.AddFlowEngineHealthChecks();

var app = builder.Build();
await app.UseFlowEngineAsync();

app.Run();
