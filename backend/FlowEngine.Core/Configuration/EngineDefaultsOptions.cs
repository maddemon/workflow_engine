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
}
