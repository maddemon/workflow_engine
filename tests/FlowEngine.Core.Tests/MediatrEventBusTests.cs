using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Events;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Core.Tests;

/// <summary>
/// MediatrEventBus 测试 —— 验证 Publish 经 IMediator 分派到 INotificationHandler，
/// 且 Subscribe 兼容垫片仍能将事件投递给动态订阅者。
/// </summary>
public class MediatrEventBusTests
{
    private sealed class TestStartedHandler : INotificationHandler<WorkflowStartedEvent>
    {
        public TaskCompletionSource<WorkflowStartedEvent>? Tcs { get; set; }

        public Task Handle(WorkflowStartedEvent notification, CancellationToken ct)
        {
            Tcs?.TrySetResult(notification);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PublishAsync_Dispatches_To_NotificationHandler()
    {
        var handler = new TestStartedHandler();
        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(MediatrEventBus).Assembly));
        services.AddSingleton<INotificationHandler<WorkflowStartedEvent>>(handler);
        services.AddSingleton<IEventBus, MediatrEventBus>();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var bus = provider.GetRequiredService<IEventBus>();
        var tcs = new TaskCompletionSource<WorkflowStartedEvent>();
        handler.Tcs = tcs;

        var evt = new WorkflowStartedEvent(Guid.NewGuid(), Guid.NewGuid());
        await bus.PublishAsync(evt, TestContext.Current.CancellationToken);

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(evt.ExecutionId, received.ExecutionId);
    }

    [Fact]
    public async Task PublishAsync_Subscribe_Shim_Still_Receives_Event()
    {
        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(MediatrEventBus).Assembly));
        services.AddSingleton<IEventBus, MediatrEventBus>();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var bus = (MediatrEventBus)provider.GetRequiredService<IEventBus>();
        var tcs = new TaskCompletionSource<WorkflowStartedEvent>();
        using var sub = bus.Subscribe<WorkflowStartedEvent>((e, _) =>
        {
            tcs.TrySetResult(e);
            return Task.CompletedTask;
        });

        var evt = new WorkflowStartedEvent(Guid.NewGuid(), Guid.NewGuid());
        await bus.PublishAsync(evt, TestContext.Current.CancellationToken);

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(evt.ExecutionId, received.ExecutionId);

        sub.Dispose();
    }

    [Fact]
    public async Task PublishAsync_HandlerThrows_DoesNotPropagateToPublisher()
    {
        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(MediatrEventBus).Assembly));
        services.AddSingleton<INotificationHandler<WorkflowStartedEvent>>(
            new ThrowingHandler<WorkflowStartedEvent>());
        services.AddSingleton<IEventBus, MediatrEventBus>();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var bus = provider.GetRequiredService<IEventBus>();

        var evt = new WorkflowStartedEvent(Guid.NewGuid(), Guid.NewGuid());

        // 异常被隔离，发布方不应收到异常。
        await bus.PublishAsync(evt, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PublishAsync_SubscribeShim_CanUnsubscribe()
    {
        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(MediatrEventBus).Assembly));
        services.AddSingleton<IEventBus, MediatrEventBus>();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var bus = (MediatrEventBus)provider.GetRequiredService<IEventBus>();
        var tcs = new TaskCompletionSource<WorkflowStartedEvent>();
        var sub = bus.Subscribe<WorkflowStartedEvent>((e, _) =>
        {
            tcs.TrySetResult(e);
            return Task.CompletedTask;
        });

        sub.Dispose();

        var evt = new WorkflowStartedEvent(Guid.NewGuid(), Guid.NewGuid());
        await bus.PublishAsync(evt, TestContext.Current.CancellationToken);

        // 取消订阅后不应再收到事件。
        await Assert.ThrowsAsync<TimeoutException>(() =>
            tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublishAsync_CovariantEvent_DeliversToBaseEventSubscriber()
    {
        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(MediatrEventBus).Assembly));
        services.AddSingleton<IEventBus, MediatrEventBus>();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var bus = (MediatrEventBus)provider.GetRequiredService<IEventBus>();
        var tcs = new TaskCompletionSource<IDomainEvent>();
        using var sub = bus.Subscribe<IDomainEvent>((e, _) =>
        {
            tcs.TrySetResult(e);
            return Task.CompletedTask;
        });

        var evt = new WorkflowStartedEvent(Guid.NewGuid(), Guid.NewGuid());
        await bus.PublishAsync(evt, TestContext.Current.CancellationToken);

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.IsType<WorkflowStartedEvent>(received);
    }

    [Fact]
    public async Task PublishAsync_ConcurrentSubscribers_AllReceive()
    {
        var services = new ServiceCollection();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(MediatrEventBus).Assembly));
        services.AddSingleton<IEventBus, MediatrEventBus>();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var bus = (MediatrEventBus)provider.GetRequiredService<IEventBus>();
        var tcs1 = new TaskCompletionSource<WorkflowStartedEvent>();
        var tcs2 = new TaskCompletionSource<WorkflowStartedEvent>();
        using var sub1 = bus.Subscribe<WorkflowStartedEvent>((e, _) =>
        {
            tcs1.TrySetResult(e);
            return Task.CompletedTask;
        });
        using var sub2 = bus.Subscribe<WorkflowStartedEvent>((e, _) =>
        {
            tcs2.TrySetResult(e);
            return Task.CompletedTask;
        });

        var evt = new WorkflowStartedEvent(Guid.NewGuid(), Guid.NewGuid());
        await bus.PublishAsync(evt, TestContext.Current.CancellationToken);

        var r1 = await tcs1.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var r2 = await tcs2.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(evt.ExecutionId, r1.ExecutionId);
        Assert.Equal(evt.ExecutionId, r2.ExecutionId);
    }

    private sealed class ThrowingHandler<T> : INotificationHandler<T>
        where T : INotification
    {
        public Task Handle(T notification, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Handler failure");
    }
}
