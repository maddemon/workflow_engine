using System.Collections.Concurrent;
using FlowEngine.Core.Entities;

namespace FlowEngine.Runtime.WaitingArea;

/// <summary>
/// 多输入节点等待区。
/// </summary>
public sealed class WaitingArea
{
    private readonly ConcurrentDictionary<(Guid ExecutionId, string NodeInstanceId), PortState> _states = new();
    private readonly TimeSpan _timeout;

    /// <summary>
    /// 初始化等待区。
    /// </summary>
    /// <param name="timeout">输入等待超时，默认 5 分钟。</param>
    public WaitingArea(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// 接收指定端口的数据批次。
    /// </summary>
    public void Receive(Guid executionId, string nodeInstanceId, string portName, DataBatch data)
    {
        var state = _states.GetOrAdd((executionId, nodeInstanceId), _ => new PortState());
        state.AddOrMerge(portName, data);
    }

    /// <summary>
    /// 判断指定节点的所有必需输入端口是否都已到齐。
    /// </summary>
    public bool IsReady(Guid executionId, string nodeInstanceId, IEnumerable<string> requiredPorts)
    {
        if (!_states.TryGetValue((executionId, nodeInstanceId), out var state))
        {
            return false;
        }

        return requiredPorts.All(state.HasData);
    }

    /// <summary>
    /// 取出指定节点已收集的输入。
    /// </summary>
    public bool TryTake(
        Guid executionId,
        string nodeInstanceId,
        out IReadOnlyDictionary<string, DataBatch> inputs)
    {
        if (!_states.TryRemove((executionId, nodeInstanceId), out var state))
        {
            inputs = new Dictionary<string, DataBatch>();
            return false;
        }

        inputs = state.GetInputs();
        return true;
    }

    /// <summary>
    /// 取消指定节点的等待。
    /// </summary>
    public void CancelWaiting(Guid executionId, string nodeInstanceId)
    {
        _states.TryRemove((executionId, nodeInstanceId), out _);
    }

    /// <summary>
    /// 获取已超时的等待项键。
    /// </summary>
    public IEnumerable<(Guid ExecutionId, string NodeInstanceId)> GetTimeoutKeys()
    {
        var now = DateTime.UtcNow;
        foreach (var (key, state) in _states)
        {
            if (now - state.LastActivity > _timeout)
            {
                yield return key;
            }
        }
    }

    /// <summary>
    /// 清理指定执行的所有等待条目。
    /// </summary>
    public void CleanupExecution(Guid executionId)
    {
        var keys = _states.Keys.Where(k => k.ExecutionId == executionId).ToList();
        foreach (var key in keys)
        {
            _states.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// 当前等待区是否为空。
    /// </summary>
    public bool IsEmpty => _states.IsEmpty;

    /// <summary>
    /// 计算等待区中最早超时项的剩余等待时长（CON-6）。用于空闲轮询的自适应唤醒：
    /// 下一次唤醒不晚于该时长，确保超时节点仍能被及时处理，同时避免无意义的固定 500ms 轮询。
    /// 等待区为空时返回 <see cref="Timeout.InfiniteTimeSpan"/>。
    /// </summary>
    public TimeSpan GetMinRemainingTimeoutDelay()
    {
        if (_states.IsEmpty)
        {
            return Timeout.InfiniteTimeSpan;
        }

        var now = DateTime.UtcNow;
        var min = _timeout;
        foreach (var (_, state) in _states)
        {
            var remaining = _timeout - (now - state.LastActivity);
            if (remaining < min)
            {
                min = remaining;
            }
        }

        return min < TimeSpan.Zero ? TimeSpan.Zero : min;
    }

    private sealed class PortState
    {
        private readonly ConcurrentDictionary<string, DataBatch> _inputs = new(StringComparer.OrdinalIgnoreCase);

        public DateTime LastActivity { get; private set; } = DateTime.UtcNow;

        public void AddOrMerge(string portName, DataBatch data)
        {
            LastActivity = DateTime.UtcNow;

            // 并发合并：AddOrUpdate 保证同一端口的「读取-合并-写入」原子完成，
            // Merge 为纯函数（不修改入参），可安全被并发重试调用。
            _inputs.AddOrUpdate(
                portName,
                data,
                (_, existing) => Merge(existing, data));
        }

        public bool HasData(string portName) => _inputs.ContainsKey(portName);

        public IReadOnlyDictionary<string, DataBatch> GetInputs() =>
            new Dictionary<string, DataBatch>(_inputs, StringComparer.OrdinalIgnoreCase);

        private static DataBatch Merge(DataBatch existing, DataBatch data)
            => DataBatch.Merge(existing, data);
    }
}
