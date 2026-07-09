using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Host.Webhooks;

/// <summary>
/// Webhook 动态路由中间件（A14）。
/// 取代启动期逐条静态映射端点的方式：对每个可能为 Webhook 的请求，
/// 在请求时按路径实时派发到 <see cref="IWebhookHandler"/>。
/// 由于处理器内部按路径查询数据库，运行时新增/删除的路由立即生效，无需重启。
/// </summary>
public sealed class WebhookRoutingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<WebhookRoutingMiddleware> _logger;

    // 保留前缀集中定义在 RouteConstants.ReservedPrefixes（与启动期 MapGet/Map 路由同源），
    // 避免拦截 API、健康检查与 WebSocket 流量，也防止与路由定义漂移（B7）。
    // 无尾斜杠的前缀（如 "/health"）要求其后紧跟路径分隔符或字符串结尾，
    // 避免 "/healthcare" 这类路径被误判为保留前缀而跳过 Webhook 派发。

    public WebhookRoutingMiddleware(RequestDelegate next, ILogger<WebhookRoutingMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Webhook 路由固定为 POST；GET 导航、API、健康检查、WebSocket 等请求直接放行，
        // 既保留既有行为，又避免对每条非 Webhook 请求做无谓派发。
        if (!HttpMethods.IsPost(context.Request.Method) ||
            string.IsNullOrWhiteSpace(path) ||
            IsReservedPath(path))
        {
            await _next(context);
            return;
        }

        _logger.LogDebug("Webhook 动态路由派发: Path={Path}", path);
        var handler = context.RequestServices.GetRequiredService<IWebhookHandler>();
        await handler.HandleAsync(context, path);
        // 处理器内部已写入响应，此处不调用 _next，避免继续进入 SPA Fallback。
    }

    private static bool IsReservedPath(string path)
    {
        foreach (var prefix in RouteConstants.ReservedPrefixes)
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 无尾斜杠前缀需确认其后紧跟路径分隔符或路径结尾，
            // 防止 "/health" 误伤 "/healthcare" 这类合法 Webhook 路径。
            if (!prefix.EndsWith("/") && path.Length > prefix.Length && path[prefix.Length] != '/')
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
