using FlowEngine.Host;
using FlowEngine.Host.Observability;
using FlowEngine.Infrastructure.DependencyInjection;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 容器 / Fly.io 部署：读取 PORT 环境变量（Fly 默认 8080）并绑定 0.0.0.0，
// 否则 Kestrel 在容器内默认仅监听 localhost:5000，外部（含 Fly 探活）无法访问。
// 本地开发未设置 PORT 时不覆盖，沿用 launchSettings 的 8001。
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// O-1：以 Serilog 作为日志提供方，配置来自 appsettings（保留 Console 接收端）。
builder.Host.UseSerilog((ctx, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration);
    // 始终保留 Console 接收端，确保日志不丢失（O-1）。
    cfg.WriteTo.Console();
});

builder.Services.AddFlowEngine(builder.Configuration, builder.Environment);

// Phase 3：注册节点执行管线相关的独立 DI 服务（凭据/共享内存/递归保护/HTTP/子执行）。
builder.Services.AddPipelineServices(builder.Configuration);

// O-2：OpenTelemetry 分布式追踪与指标（ASP.NET Core + HttpClient 仪表，stdout 导出）。
builder.Services.AddFlowEngineOpenTelemetry(builder.Configuration);

// O-6：健康检查（liveness 存活探针 + readiness 就绪探针含数据库探测）。
builder.Services.AddFlowEngineHealthChecks();

var app = builder.Build();

await app.UseFlowEngineAsync();

app.Run();
