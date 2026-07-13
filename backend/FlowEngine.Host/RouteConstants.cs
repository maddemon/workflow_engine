namespace FlowEngine.Host;

/// <summary>
/// 宿主路由相关的共享常量，集中定义保留前缀与映射路径，
/// 避免 Webhook 动态路由的保留前缀检查与实际的 MapGet/Map 路由定义发生漂移（B7）。
/// </summary>
public static class RouteConstants
{
    /// <summary>API 路由组前缀（不含版本段），如 "/api"。</summary>
    public const string ApiPrefix = "/api";

    /// <summary>健康检查前缀，如 "/health"。</summary>
    public const string HealthPrefix = "/health";

    /// <summary>健康检查就绪端点（完整路径）。</summary>
    public const string HealthReadyPath = "/health/ready";

    /// <summary>WebSocket 路由前缀，如 "/ws"。</summary>
    public const string WebSocketPrefix = "/ws";

    /// <summary>MCP (Model Context Protocol) 流式 HTTP 路由前缀，如 "/mcp"。</summary>
    public const string McpPrefix = "/mcp";

    /// <summary>
    /// Webhook 动态路由中间件跳过的保留前缀集合（边界感知，见 <see cref="Webhooks.WebhookRoutingMiddleware"/>）。
    /// 与 <see cref="ApiPrefix"/>、<see cref="HealthPrefix"/>、<see cref="WebSocketPrefix"/>、<see cref="McpPrefix"/> 同源，避免漂移。
    /// </summary>
    public static readonly IReadOnlyList<string> ReservedPrefixes = new[]
    {
        ApiPrefix + "/",        // "/api/"
        HealthPrefix,           // "/health"
        WebSocketPrefix + "/",  // "/ws/"
        McpPrefix,              // "/mcp"（MCP Streamable HTTP，POST 收发请求、GET SSE 流，需放行给 MapMcp 端点）
    };
}
