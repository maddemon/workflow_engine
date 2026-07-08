namespace FlowEngine.Host.Webhooks;

/// <summary>
/// Webhook 端点元数据，用于标识通过 <see cref="ApplicationBuilderExtensions.UseWebhook"/> 注册的路由。
/// </summary>
public sealed record WebhookEndpointMetadata(Guid WebhookRouteId)
{
    /// <summary>
    /// 标记当前端点是否为 Webhook。
    /// </summary>
    public bool IsWebhook { get; } = true;
}
