using FlowEngine.Core.Abstractions;

namespace FlowEngine.Host.Tests.Infrastructure;

/// <summary>
/// 测试专用调度管理器空实现，避免并行/重复初始化 Quartz 导致 LoggerFactory 被释放。
/// </summary>
public sealed class NoOpScheduleManager : IScheduleManager
{
    public Task<DateTime?> GetNextFireTimeAsync(Guid triggerId, CancellationToken cancellationToken = default)
        => Task.FromResult<DateTime?>(null);

    public Task RegisterPollTriggerAsync(
        Guid triggerId,
        Guid workflowDefinitionId,
        int intervalSeconds,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RegisterScheduleAsync(
        Guid triggerId,
        Guid workflowDefinitionId,
        string cronExpression,
        string? timeZone = null,
        DateTime? startAt = null,
        DateTime? endAt = null,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task UnregisterPollTriggerAsync(Guid triggerId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task UnregisterScheduleAsync(Guid triggerId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
