namespace FlowEngine.Application.RateLimiting;

/// <summary>
/// 速率限制配置选项。
/// </summary>
public class RateLimitOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// 登录接口速率限制规则。
    /// </summary>
    public RateLimitRule Login { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 };

    /// <summary>
    /// 注册接口速率限制规则。
    /// </summary>
    public RateLimitRule Register { get; set; } = new() { PermitLimit = 3, WindowSeconds = 60 };

    /// <summary>
    /// 通用 API 速率限制规则。
    /// </summary>
    public RateLimitRule Api { get; set; } = new() { PermitLimit = 100, WindowSeconds = 60 };

    /// <summary>
    /// 免受速率限制的路径列表。
    /// </summary>
    public string[] WhitelistedPaths { get; set; } = ["/health", "/health/ready"];
}

/// <summary>
/// 单条速率限制规则。
/// </summary>
public class RateLimitRule
{
    /// <summary>
    /// 窗口内允许的最大请求数。
    /// </summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>
    /// 时间窗口大小（秒）。
    /// </summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// 是否启用该规则。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 规则标识键（用于审计日志）。
    /// </summary>
    public string Key { get; set; } = string.Empty;
}
