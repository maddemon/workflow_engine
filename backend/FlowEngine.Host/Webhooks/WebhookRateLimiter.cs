using System.Collections.Concurrent;
using FlowEngine.Host.Options;
using Microsoft.Extensions.Options;

namespace FlowEngine.Host.Webhooks;

/// <summary>
/// Webhook 按路由/IP 限流（SEC-3）：固定窗口计数器，键为 <c>路由路径 + 远程 IP</c>。
/// 轻量内存实现，不引入额外 NuGet 依赖；与全局 <see cref="System.Threading.RateLimiting"/> 限流器互补，
/// 此处专门面向 Webhook 接收路径提供"每路由每客户端"的精细限制。
/// </summary>
public sealed class WebhookRateLimiter
{
    private readonly WebhookSecurityOptions _options;
    private readonly ConcurrentDictionary<string, (int Count, long WindowStart)> _counters =
        new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public WebhookRateLimiter(IOptions<WebhookSecurityOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// 尝试为指定路由与 IP 获取一个请求配额。
    /// </summary>
    /// <param name="routePath">Webhook 路由路径。</param>
    /// <param name="remoteIp">客户端 IP。</param>
    /// <param name="nowUnix">当前 Unix 秒。</param>
    /// <returns>配额充足返回 <c>true</c>；超限返回 <c>false</c>（调用方应返回 429）。</returns>
    public bool TryAcquire(string routePath, string remoteIp, long nowUnix)
    {
        if (!_options.EnableRateLimit)
        {
            return true;
        }

        var window = _options.RateLimitWindowSeconds;
        var windowStart = nowUnix - (nowUnix % window);
        var key = $"{routePath}:{remoteIp}";

        lock (_gate)
        {
            if (_counters.TryGetValue(key, out var entry) && entry.WindowStart == windowStart)
            {
                if (entry.Count >= _options.RateLimitPermitCount)
                {
                    return false;
                }

                _counters[key] = (entry.Count + 1, windowStart);
            }
            else
            {
                _counters[key] = (1, windowStart);
            }

            return true;
        }
    }
}
