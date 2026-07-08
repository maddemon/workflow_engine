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

    // 与启动期保留前缀保持一致，避免拦截 API、健康检查与 WebSocket 流量。
    private static readonly string[] ReservedPrefixes =
    {
        "/api/", "/health", "/ws/",
    };

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
            ReservedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        _logger.LogDebug("Webhook 动态路由派发: Path={Path}", path);
        var handler = context.RequestServices.GetRequiredService<IWebhookHandler>();
        await handler.HandleAsync(context, path);
        // 处理器内部已写入响应，此处不调用 _next，避免继续进入 SPA Fallback。
    }
}
