using System.Collections.Concurrent;
using FlowEngine.Core.Enums;

namespace FlowEngine.Host.Webhooks;

/// <summary>
/// <see cref="IWebhookSyncCompletionService"/> 的内存实现（EX-4）。
/// <para>
/// 用 <see cref="TaskCompletionSource{ExecutionStatus}"/> 桥接"启动执行"与"执行完成事件"，
/// 取代原先在 <see cref="WebhookHandler"/> 中周期性查询 <c>ExecutionRecords</c> 的 DB 轮询，
/// 消除每条同步 Webhook 请求对数据库的轮询压力。
/// </para>
/// <para>完成状态以短 TTL 缓存，以覆盖"注册晚于完成事件"的竞态；过期条目周期性清理。</para>
/// </summary>
public sealed class WebhookSyncCompletionService : IWebhookSyncCompletionService
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ExecutionStatus>> _pending = new();
    private readonly ConcurrentDictionary<Guid, (ExecutionStatus Status, long ExpiryTicks)> _completed = new();
    private DateTime _lastCleanup = DateTime.UtcNow;

    /// <inheritdoc />
    public Task<ExecutionStatus> WaitAsync(Guid executionId, TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<ExecutionStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[executionId] = tcs;

        // 竞态保护：若执行在注册前已结束，立即兑现，避免无限等待。
        if (_completed.TryGetValue(executionId, out var cached) && cached.ExpiryTicks > DateTime.UtcNow.Ticks)
        {
            _pending.TryRemove(executionId, out _);
            tcs.TrySetResult(cached.Status);
            return tcs.Task;
        }

        // 超时/取消时清理并失败该等待，由调用方降级为 202 Timeout。
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);
        linked.Token.Register(() =>
        {
            if (_pending.TryRemove(executionId, out var t))
            {
                // 超时与客户端断开都表示为"未在窗口内完成"，统一以 OperationCanceledException 让调用方降级为 202 Timeout。
                t.TrySetException(new OperationCanceledException("Webhook sync wait timed out."));
            }
        });

        return tcs.Task;
    }

    /// <inheritdoc />
    public void Complete(Guid executionId, ExecutionStatus status)
    {
        _completed[executionId] = (status, DateTime.UtcNow.AddMinutes(5).Ticks);

        if (_pending.TryRemove(executionId, out var tcs))
        {
            tcs.TrySetResult(status);
        }

        MaybeCleanup();
    }

    private void MaybeCleanup()
    {
        if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromSeconds(30))
        {
            return;
        }

        _lastCleanup = DateTime.UtcNow;
        var now = DateTime.UtcNow.Ticks;
        foreach (var pair in _completed)
        {
            if (pair.Value.ExpiryTicks <= now)
            {
                _completed.TryRemove(pair.Key, out _);
            }
        }
    }
}
