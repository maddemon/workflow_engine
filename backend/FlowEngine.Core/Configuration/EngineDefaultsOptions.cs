namespace FlowEngine.Core.Configuration;

/// <summary>
/// 引擎全局默认配置。
/// </summary>
public class EngineDefaultsOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "EngineDefaults";

    /// <summary>
    /// 默认节点超时（秒），null 表示不限。
    /// </summary>
    public int? DefaultTimeoutSeconds { get; set; }

    /// <summary>
    /// 默认最大重试次数。
    /// </summary>
    public int DefaultMaxRetries { get; set; } = 0;

    /// <summary>
    /// 默认基础延迟（秒）。
    /// </summary>
    public int DefaultBaseDelaySeconds { get; set; } = 1;

    /// <summary>
    /// 默认最大延迟（秒）。
    /// </summary>
    public int DefaultMaxDelaySeconds { get; set; } = 60;

    /// <summary>
    /// 单节点反馈边激活的累计上限（安全网）。超过则判定为环路失控，
    /// 执行转 Failed（错误码 <c>CycleLimitExceeded</c>）。用于兜底基于节点上下文的潜在无限回环。
    /// 0 或负值表示不限制（沿用既有行为，依赖节点自身终止条件）。
    /// </summary>
    public int MaxCycleIterations { get; set; } = 10000;
}
