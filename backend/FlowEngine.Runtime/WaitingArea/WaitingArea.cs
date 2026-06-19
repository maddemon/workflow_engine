using System.Collections.Concurrent;
using FlowEngine.Core.Entities;

namespace FlowEngine.Runtime.WaitingArea;

/// <summary>
/// 多输入节点等待区。
/// </summary>
public sealed class WaitingArea
{
    private readonly ConcurrentDictionary<(Guid ExecutionId, Guid NodeInstanceId), PortState> _states = new();
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
    public void Receive(Guid executionId, Guid nodeInstanceId, string portName, DataBatch data)
    {
        var state = _states.GetOrAdd((executionId, nodeInstanceId), _ => new PortState());
        state.AddOrMerge(portName, data);
    }

    /// <summary>
    /// 判断指定节点的所有必需输入端口是否都已到齐。
    /// </summary>
    public bool IsReady(Guid executionId, Guid nodeInstanceId, IEnumerable<string> requiredPorts)
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
        Guid nodeInstanceId,
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
    public void CancelWaiting(Guid executionId, Guid nodeInstanceId)
    {
        _states.TryRemove((executionId, nodeInstanceId), out _);
    }

    /// <summary>
    /// 获取已超时的等待项键。
    /// </summary>
    public IEnumerable<(Guid ExecutionId, Guid NodeInstanceId)> GetTimeoutKeys()
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

    private sealed class PortState
    {
        private readonly Dictionary<string, DataBatch> _inputs = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        public DateTime LastActivity { get; private set; } = DateTime.UtcNow;

        public void AddOrMerge(string portName, DataBatch data)
        {
            lock (_lock)
            {
                LastActivity = DateTime.UtcNow;

                if (_inputs.TryGetValue(portName, out var existing))
                {
                    var merged = new DataBatch();
                    merged.Items.AddRange(existing.Items);
                    merged.Items.AddRange(data.Items);

                    for (var i = 0; i < merged.Items.Count; i++)
                    {
                        merged.Items[i].SourceIndex = i;
                    }

                    _inputs[portName] = merged;
                }
                else
                {
                    _inputs[portName] = data;
                }
            }
        }

        public bool HasData(string portName)
        {
            lock (_lock)
            {
                return _inputs.ContainsKey(portName);
            }
        }

        public IReadOnlyDictionary<string, DataBatch> GetInputs()
        {
            lock (_lock)
            {
                return new Dictionary<string, DataBatch>(_inputs, StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
