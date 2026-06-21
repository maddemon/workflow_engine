namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 调度管理器，负责注册和注销定时触发器。
/// </summary>
public interface IScheduleManager
{
    /// <summary>
    /// 注册定时触发器。
    /// </summary>
    Task RegisterScheduleAsync(
        Guid triggerId,
        Guid workflowDefinitionId,
        string cronExpression,
        string? timeZone = null,
        DateTime? startAt = null,
        DateTime? endAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 注销定时触发器。
    /// </summary>
    Task UnregisterScheduleAsync(Guid triggerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取下次触发时间。
    /// </summary>
    Task<DateTime?> GetNextFireTimeAsync(Guid triggerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动调度器。
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止调度器。
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
