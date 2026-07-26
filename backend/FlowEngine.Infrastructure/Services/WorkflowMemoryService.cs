using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;

namespace FlowEngine.Infrastructure.Services;

/// <summary>
/// 跨节点共享内存实现：以 <see cref="ConcurrentDictionary{TKey,TValue}"/> 存储 JsonNode，
/// 提供类型安全的读写接口，供单次工作流执行内的多节点共享状态。
/// </summary>
public sealed class WorkflowMemoryService : IWorkflowMemoryService
{
    private readonly ConcurrentDictionary<string, JsonNode?> _store = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public T? Get<T>(string key)
    {
        if (!_store.TryGetValue(key, out var node) || node is null)
        {
            return default;
        }

        if (node is JsonValue && typeof(T) == typeof(JsonNode))
        {
            return (T?)(object?)node;
        }

        return node.Deserialize<T>(JsonDefaults.Options);
    }

    /// <inheritdoc />
    public void Set<T>(string key, T value)
    {
        _store[key] = value is null ? null : JsonSerializer.SerializeToNode(value, JsonDefaults.Options);
    }

    /// <inheritdoc />
    public IEnumerable<KeyValuePair<string, JsonNode?>> Snapshot() => _store.ToArray();
}
