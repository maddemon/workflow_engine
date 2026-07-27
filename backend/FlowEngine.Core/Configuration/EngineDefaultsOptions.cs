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

    /// <summary>
    /// 单个节点在 <c>SuccessfulOutputs</c> / <c>LatestBatches</c> 中保留输出项数的默认上限（CON-5）。
    /// 供 <see cref="MaxRetainedOutputItems"/> 在未显式配置（0 或负值）时回退使用，
    /// 保证上限始终生效、不会因配置退化为无界增长。
    /// </summary>
    public const int DefaultMaxRetainedOutputItems = 1000;

    /// <summary>
    /// 单个节点在 <c>SuccessfulOutputs</c> / <c>LatestBatches</c> 中保留的最大输出项数（CON-5）。
    /// 超过后仅保留最新 N 项，以限制大批次（如大 OncePerItem 输入）常驻内存。
    /// 默认值 <see cref="DefaultMaxRetainedOutputItems"/>（1000），确保默认环境下大批次输出内存上限生效。
    /// 显式配置为 0 或负值时表示"采用默认上限"（<see cref="DefaultMaxRetainedOutputItems"/>），
    /// 即上限始终启用，不会因配置而退化为无界增长。
    /// 累积键为节点 <see cref="FlowEngine.Core.Entities.NodeDefinition.Id"/>（稳定唯一标识），
    /// 而非 <see cref="FlowEngine.Core.Entities.NodeDefinition.Name"/>，避免同名不同节点互相覆盖。
    /// </summary>
    public int MaxRetainedOutputItems { get; set; } = DefaultMaxRetainedOutputItems;

    /// <summary>
    /// 工作流执行后台服务并发消费队列的最大并行度（CON-2）。
    /// 多个执行项可并行处理，互不阻塞；0 或负值回退为默认值 4。
    /// </summary>
    public int MaxWorkerConcurrency { get; set; } = 4;
}
