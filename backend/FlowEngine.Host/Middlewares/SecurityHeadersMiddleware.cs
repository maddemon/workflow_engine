namespace FlowEngine.Host.Middlewares;

/// <summary>
/// 安全响应头中间件，添加常用安全头以增强应用安全性。
/// </summary>
public class SecurityHeadersMiddleware(
    RequestDelegate next,
    IWebHostEnvironment environment)
{
    /// <summary>
    /// 处理请求并添加安全响应头。
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.XFrameOptions = "DENY";
        context.Response.Headers.XXSSProtection = "1; mode=block";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // 开发环境放宽 CSP 以允许内联脚本与 eval（Vite/HMR 调试需要），生产环境严格限制。
        context.Response.Headers["Content-Security-Policy"] = environment.IsDevelopment()
            ? "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; connect-src 'self' ws: wss:"
            : "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'";

        // 仅在生产/非开发环境启用 HSTS，强制客户端通过 HTTPS 访问，防止降级攻击。
        // 开发环境（自承载/本地调试）不应下发，避免浏览器缓存导致的本地 HTTP 访问困难。
        if (!environment.IsDevelopment())
        {
            context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        }

        await next(context);
    }
}
