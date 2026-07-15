using FlowEngine.Core.Abstractions;

namespace FlowEngine.Application.Tests.TestSupport.Fakes;

public sealed class FakeScheduleManager : IScheduleManager
{
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RegisterScheduleAsync(
        Guid triggerId,
        Guid workflowDefinitionId,
        string cronExpression,
        string? timeZone = null,
        DateTime? startAt = null,
        DateTime? endAt = null,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task UnregisterScheduleAsync(Guid triggerId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<DateTime?> GetNextFireTimeAsync(Guid triggerId, CancellationToken cancellationToken = default)
        => Task.FromResult<DateTime?>(null);

    public Task RegisterPollTriggerAsync(
        Guid triggerId,
        Guid workflowDefinitionId,
        int intervalSeconds,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task UnregisterPollTriggerAsync(Guid triggerId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
