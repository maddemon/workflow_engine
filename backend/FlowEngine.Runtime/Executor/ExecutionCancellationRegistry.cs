using System.Collections.Concurrent;
using System.Threading;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 按 <c>executionId</c> 索引的执行取消令牌注册表（线程安全单例）。
/// 后台 worker 在驱动某次执行时登记对应的 <see cref="CancellationTokenSource"/>，
/// <see cref="ExecutionService.CancelAsync"/> 通过 <see cref="TryCancel"/> 触发取消，
/// 使运行中的执行可被真正取消并进入 <c>Cancelled</c> 终态。
/// </summary>
public sealed class ExecutionCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _sources = new();

    /// <summary>
    /// 登记某次执行的取消令牌源。若同一 <c>executionId</c> 已存在登记，先释放旧源再覆盖，避免令牌源泄漏。
    /// </summary>
    public void Register(Guid executionId, CancellationTokenSource cts)
    {
        _sources.AddOrUpdate(executionId, cts, (_, old) =>
        {
            try
            {
                old.Dispose();
            }
            catch
            {
                // 释放失败不影响新的登记。
            }

            return cts;
        });
    }

    /// <summary>
    /// 取消指定执行。若已登记对应的令牌源则返回 <c>true</c>（执行正在运行）；
    /// 否则（执行尚未出队或已结束）返回 <c>false</c>。
    /// </summary>
    public bool TryCancel(Guid executionId)
    {
        if (_sources.TryGetValue(executionId, out var cts))
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 源可能已被 Unregister 释放（执行已结束/worker 退出），
                // 此时取消已无意义，忽略即可。若不清此异常，CancelAsync 与 worker
                // 完成解除登记并发时会抛出 ObjectDisposedException。
            }
            catch (AggregateException)
            {
                // 取消已触发，忽略聚合异常。
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// 移除指定执行的登记（执行结束 / worker 退出时调用），并释放令牌源。
    /// </summary>
    public void Unregister(Guid executionId)
    {
        if (_sources.TryRemove(executionId, out var cts))
        {
            try
            {
                cts.Dispose();
            }
            catch
            {
                // 释放失败可忽略。
            }
        }
    }
}
