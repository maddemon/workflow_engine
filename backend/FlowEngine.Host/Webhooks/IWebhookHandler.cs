namespace FlowEngine.Host.Webhooks;

/// <summary>
/// Webhook 请求处理器抽象，便于在动态路由中间件与测试中替换实现。
/// </summary>
public interface IWebhookHandler
{
    /// <summary>
    /// 处理 Webhook 请求。
    /// </summary>
    /// <param name="context">当前 HTTP 上下文。</param>
    /// <param name="routePath">请求路径，用于运行时动态路由（A14）。</param>
    Task HandleAsync(HttpContext context, string routePath);
}
