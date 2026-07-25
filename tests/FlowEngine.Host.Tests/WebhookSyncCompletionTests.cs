using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Host.Webhooks;

namespace FlowEngine.Host.Tests;

/// <summary>
/// Webhook 同步完成服务与通知处理器的单元测试（EX-4）。
/// 验证"以事件通知替换 DB 轮询"的唤醒往返：等待者经 <see cref="IWebhookSyncCompletionService.Complete"/>
/// 被完成事件唤醒，且竞态（先完成后等待）与超时均被正确处理。
/// </summary>
public class WebhookSyncCompletionTests
{
    [Fact]
    public async Task WaitAsync_Completes_WhenCompleted()
    {
        var service = new WebhookSyncCompletionService();
        var executionId = Guid.NewGuid();

        var waitTask = service.WaitAsync(executionId, Timeout.InfiniteTimeSpan, CancellationToken.None);
        service.Complete(executionId, ExecutionStatus.Completed);

        var status = await waitTask;
        Assert.Equal(ExecutionStatus.Completed, status);
    }

    [Fact]
    public async Task WaitAsync_ReturnsImmediately_WhenAlreadyCompleted()
    {
        var service = new WebhookSyncCompletionService();
        var executionId = Guid.NewGuid();

        // 竞态保护：完成事件早于等待注册，应立即兑现缓存结果，避免无限等待。
        service.Complete(executionId, ExecutionStatus.Failed);

        var status = await service.WaitAsync(executionId, Timeout.InfiniteTimeSpan, CancellationToken.None);
        Assert.Equal(ExecutionStatus.Failed, status);
    }

    [Fact]
    public async Task WaitAsync_ThrowsOperationCanceled_OnTimeout()
    {
        var service = new WebhookSyncCompletionService();
        var executionId = Guid.NewGuid();

        var waitTask = service.WaitAsync(executionId, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        await Assert.ThrowsAsync<OperationCanceledException>(() => waitTask);
    }

    [Fact]
    public async Task Notifier_Handle_CompletesPendingWaiter()
    {
        var service = new WebhookSyncCompletionService();
        var notifier = new WebhookCompletionNotifier(service);
        var executionId = Guid.NewGuid();

        var waitTask = service.WaitAsync(executionId, Timeout.InfiniteTimeSpan, CancellationToken.None);

        await notifier.Handle(
            new WorkflowCompletedEvent(executionId, Guid.NewGuid(), ExecutionStatus.Completed),
            CancellationToken.None);

        var status = await waitTask;
        Assert.Equal(ExecutionStatus.Completed, status);
    }
}
