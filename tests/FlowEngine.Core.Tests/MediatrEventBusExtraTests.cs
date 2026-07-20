using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Events;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Core.Tests;

/// <summary>
/// <see cref="MediatrEventBus"/> 额外行为测试，覆盖主测试未触及的分支：
/// <list type="bullet">
///   <item><description>事件未实现 <see cref="INotification"/> 时，PublishToMediatorAsync 提前返回（不进 MediatR 分派）。</description></item>
///   <item><description>通知处理器抛出 <see cref="OperationCanceledException"/> 时透传给发布方（区别于普通异常被隔离）。</description></item>
///   <item><description>动态订阅者抛异常时被隔离（记录日志，不影响其他订阅者与发布方）。</description></item>
/// </list>
/// </summary>
public class MediatrEventBusExtraTests
{
    /// <summary>
    /// 仅实现 <see cref="IDomainEvent"/>、不实现 <see cref="INotification"/> 的事件，用于触发提前返回分支。
    /// </summary>
    private sealed class PlainDomainEvent : IDomainEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    private sealed class CancellingHandler : INotificationHandler<WorkflowStartedEvent>
    {
        public Task Handle(WorkflowStartedEvent notification, CancellationToken cancellationToken)
            => throw new OperationCanceledException("cancelled");
    }

    private static MediatrEventBus BuildBus()
    {
        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(MediatrEventBus).Assembly));
        services.AddSingleton<IEventBus, MediatrEventBus>();
        services.AddLogging();
        return (MediatrEventBus)services.BuildServiceProvider().GetRequiredService<IEventBus>();
    }

    [Fact]
    public async Task PublishAsync_NonNotificationEvent_SkipsMediatorButDispatchesToSubscribers()
    {
        var bus = BuildBus();
        var tcs = new TaskCompletionSource<IDomainEvent>();
        using var sub = bus.Subscribe<PlainDomainEvent>((e, _) =>
        {
            tcs.TrySetResult(e);
            return Task.CompletedTask;
        });

        var evt = new PlainDomainEvent();
        await bus.PublishAsync(evt, TestContext.Current.CancellationToken);

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Same(evt, received);
    }

    [Fact]
    public async Task PublishAsync_HandlerThrowsOperationCanceled_PropagatesToPublisher()
    {
        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(MediatrEventBus).Assembly));
        services.AddSingleton<INotificationHandler<WorkflowStartedEvent>>(new CancellingHandler());
        services.AddSingleton<IEventBus, MediatrEventBus>();
        services.AddLogging();
        var bus = (MediatrEventBus)services.BuildServiceProvider().GetRequiredService<IEventBus>();

        var evt = new WorkflowStartedEvent(Guid.NewGuid(), Guid.NewGuid());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => bus.PublishAsync(evt, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublishAsync_SubscriberThrows_IsolatedFromOtherSubscribers()
    {
        var bus = BuildBus();
        var goodTcs = new TaskCompletionSource<WorkflowStartedEvent>();
        var badTcs = new TaskCompletionSource<bool>();

        using var badSub = bus.Subscribe<WorkflowStartedEvent>((_, _) =>
            throw new InvalidOperationException("subscriber boom"));
        using var goodSub = bus.Subscribe<WorkflowStartedEvent>((e, _) =>
        {
            goodTcs.TrySetResult(e);
            return Task.CompletedTask;
        });

        var evt = new WorkflowStartedEvent(Guid.NewGuid(), Guid.NewGuid());

        // 异常被隔离：发布方不应收到异常，且正常订阅者仍收到事件。
        await bus.PublishAsync(evt, TestContext.Current.CancellationToken);

        var received = await goodTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(evt.ExecutionId, received.ExecutionId);
    }
}
