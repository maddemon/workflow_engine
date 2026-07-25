using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FlowEngine.Host.Middlewares;

/// <summary>
/// 请求访问日志中间件（E-4）：记录 HTTP 方法、路径、状态码与耗时，排除健康检查端点。
/// 出于安全约束，仅记录路径，不记录查询字符串（可能含令牌/凭据）与任何负载。
/// </summary>
public sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    /// <summary>
    /// 处理请求并记录访问日志（健康检查端点跳过）。
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // 健康检查端点高频且无需审计，直接跳过请求日志（E-4）。
        if (IsHealthPath(context.Request.Path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            logger.LogInformation(
                "HTTP {Method} {Path} 完成 {StatusCode} 耗时 {ElapsedMs}ms",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds);
        }
    }

    private static bool IsHealthPath(PathString path) =>
        path.StartsWithSegments(RouteConstants.HealthPrefix);
}
