namespace FlowEngine.Application.ExecutionCleanup;

/// <summary>
/// 执行清理配置选项。
/// </summary>
public class ExecutionCleanupOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "ExecutionCleanup";

    /// <summary>
    /// 是否启用执行清理。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 清理间隔（分钟）。
    /// </summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>
    /// 执行记录保留天数。
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// 每个工作流保留的最大记录数。
    /// </summary>
    public int MaxRecordsToKeep { get; set; } = 10000;

    /// <summary>
    /// 分批删除时每批删除的记录数，避免一次性删除大量数据导致超时或锁表。
    /// </summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// 分批删除时批次间的延迟（毫秒），用于减少数据库压力。
    /// </summary>
    public int BatchDelayMs { get; set; } = 100;
}
