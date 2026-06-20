using FlowEngine.Core.Events;

namespace FlowEngine.Core.Tests;

/// <summary>
/// InMemoryEventBus 测试 —— 覆盖发布订阅、异常隔离、Dispose。
/// </summary>
public class InMemoryEventBusTests
{
    [Fact]
    public async Task PublishAsync_Subscriber_Receives_Event()
    {
        var errorSink = new InternalErrorSink();
        using var bus = new InMemoryEventBus(errorSink);
        var ct = TestContext.Current.CancellationToken;
        var tcs = new TaskCompletionSource<WorkflowStartedEvent>();

        bus.Subscribe<WorkflowStartedEvent>((e, _) =>
        {
            tcs.TrySetResult(e);
            return Task.CompletedTask;
        });

        var evt = new WorkflowStartedEvent(Guid.NewGuid(), Guid.NewGuid());
        await bus.PublishAsync(evt, ct);

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Equal(evt.ExecutionId, result.ExecutionId);
    }

    [Fact]
    public async Task PublishAsync_Multiple_Events_All_Delivered()
    {
        var errorSink = new InternalErrorSink();
        using var bus = new InMemoryEventBus(errorSink);
        var ct = TestContext.Current.CancellationToken;
        var count = 0;
        var tcs = new TaskCompletionSource();

        bus.Subscribe<WorkflowStartedEvent>((_, _) =>
        {
            if (Interlocked.Increment(ref count) == 3)
            {
                tcs.TrySetResult();
            }
            return Task.CompletedTask;
        });

        await bus.PublishAsync(new WorkflowStartedEvent(Guid.NewGuid(), Guid.NewGuid()), ct);
        await bus.PublishAsync(new WorkflowStartedEvent(Guid.NewGuid(), Guid.NewGuid()), ct);
        await bus.PublishAsync(new WorkflowStartedEvent(Guid.NewGuid(), Guid.NewGuid()), ct);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task Unsubscribe_Stops_Receiving_Events()
    {
        var errorSink = new InternalErrorSink();
        using var bus = new InMemoryEventBus(errorSink);
        var ct = TestContext.Current.CancellationToken;
        var count = 0;
        var firstReceived = new TaskCompletionSource();

        var sub = bus.Subscribe<WorkflowStartedEvent>((_, _) =>
        {
            Interlocked.Increment(ref count);
            firstReceived.TrySetResult();
            return Task.CompletedTask;
        });

        await bus.PublishAsync(new WorkflowStartedEvent(Guid.NewGuid(), Guid.NewGuid()), ct);
        await firstReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);

        sub.Dispose();

        await bus.PublishAsync(new WorkflowStartedEvent(Guid.NewGuid(), Guid.NewGuid()), ct);
        await Task.Delay(200, ct);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Dispose_Completes_Without_Exception()
    {
        var errorSink = new InternalErrorSink();
        var bus = new InMemoryEventBus(errorSink);
        bus.Dispose();
    }
}
