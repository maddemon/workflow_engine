namespace FlowEngine.Host.Options;

/// <summary>
/// Webhook 安全加固配置（SEC-3）：重放保护与按路由/IP 限流。
/// 全部默认开启，可经配置节 <c>Webhook:Security</c> 关闭或调参。
/// </summary>
public sealed class WebhookSecurityOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Webhook:Security";

    /// <summary>
    /// 是否启用重放保护。启用后要求请求携带 <c>X-Webhook-Timestamp</c> 与 <c>X-Webhook-Nonce</c>，
    /// 并拒绝过期时间戳与已使用过的 nonce。默认 <c>true</c>。
    /// </summary>
    public bool EnableReplayProtection { get; set; } = true;

    /// <summary>重放窗口（秒）：时间戳早于此窗口即视为过期。默认 300 秒。</summary>
    public int ReplayWindowSeconds { get; set; } = 300;

    /// <summary>允许的最大时钟偏差（秒），容忍客户端与服务端时间不同步。默认 30 秒。</summary>
    public int MaxClockSkewSeconds { get; set; } = 30;

    /// <summary>是否启用按路由/IP 的限流。默认 <c>true</c>。</summary>
    public bool EnableRateLimit { get; set; } = true;

    /// <summary>限流窗口内允许的最大请求数（每个 路由+IP）。默认 60。</summary>
    public int RateLimitPermitCount { get; set; } = 60;

    /// <summary>限流窗口长度（秒）。默认 60 秒。</summary>
    public int RateLimitWindowSeconds { get; set; } = 60;
}
