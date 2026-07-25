using System.Collections.Concurrent;
using FlowEngine.Host.Options;
using Microsoft.Extensions.Options;

namespace FlowEngine.Host.Webhooks;

/// <summary>
/// Webhook 重放保护（SEC-3）：轻量内存缓存，记录已使用的 <c>(路由路径, nonce)</c> 与对应的有效期时间戳。
/// <list type="bullet">
///   <item>拒绝过期时间戳（超出 <see cref="WebhookSecurityOptions.ReplayWindowSeconds"/> 或允许时钟偏差）。</item>
///   <item>拒绝已见过的 nonce（重放），确保同一请求无法被捕获后重复提交。</item>
/// </list>
/// 不引入额外 NuGet 依赖，单实例（Singleton）在进程内共享；使用固定窗口的 TTL 自动清理过期条目。
/// </summary>
public sealed class WebhookReplayCache
{
    private readonly WebhookSecurityOptions _options;
    private readonly ConcurrentDictionary<string, long> _seen = new(StringComparer.Ordinal);
    private DateTime _lastCleanup = DateTime.UtcNow;

    public WebhookReplayCache(IOptions<WebhookSecurityOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// 校验时间戳新鲜度与 nonce 唯一性。
    /// </summary>
    /// <param name="routePath">Webhook 路由路径。</param>
    /// <param name="nonce">请求携带的 nonce（<c>X-Webhook-Nonce</c>）。</param>
    /// <param name="timestampUnix">请求携带的时间戳（Unix 秒，<c>X-Webhook-Timestamp</c>）。</param>
    /// <param name="nowUnix">当前 Unix 秒。</param>
    /// <param name="error">失败时返回原因（仅用于日志/审计，不暴露敏感信息）。</param>
    /// <returns>校验通过返回 <c>true</c>。</returns>
    public bool TryAccept(string routePath, string nonce, long timestampUnix, long nowUnix, out string? error)
    {
        if (!_options.EnableReplayProtection)
        {
            error = null;
            return true;
        }

        if (timestampUnix < nowUnix - _options.ReplayWindowSeconds
            || timestampUnix > nowUnix + _options.MaxClockSkewSeconds)
        {
            error = "timestamp expired or outside allowed clock skew";
            return false;
        }

        var key = $"{routePath}:{nonce}";
        var expiry = nowUnix + _options.ReplayWindowSeconds;

        // TryAdd 原子地保证同一 nonce 仅可被接受一次；已存在即判定为重放。
        if (!_seen.TryAdd(key, expiry))
        {
            error = "nonce already used (possible replay)";
            return false;
        }

        MaybeCleanup(nowUnix);
        error = null;
        return true;
    }

    private void MaybeCleanup(long nowUnix)
    {
        // 周期性清理过期条目，避免缓存无限增长。
        if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromSeconds(30))
        {
            return;
        }

        _lastCleanup = DateTime.UtcNow;
        foreach (var pair in _seen)
        {
            if (pair.Value <= nowUnix)
            {
                _seen.TryRemove(pair.Key, out _);
            }
        }
    }
}
