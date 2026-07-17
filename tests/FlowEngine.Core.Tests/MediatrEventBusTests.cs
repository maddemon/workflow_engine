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
}
