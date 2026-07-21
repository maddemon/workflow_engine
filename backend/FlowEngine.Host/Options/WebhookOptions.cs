namespace FlowEngine.Host.Options;

/// <summary>
/// Webhook 处理相关配置选项。
/// </summary>
public sealed class WebhookOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "Webhook";

    /// <summary>
    /// 同步轮询执行结果时每次等待的间隔（毫秒）。
    /// 原硬编码为 100ms，提取为可配置项以便在高负载场景下调优。
    /// </summary>
    public int PollingIntervalMs { get; set; } = 100;
}
