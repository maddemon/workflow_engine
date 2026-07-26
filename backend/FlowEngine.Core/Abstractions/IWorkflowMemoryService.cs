using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 跨节点共享内存：单次工作流执行内各节点可读写的共享状态，
/// 取代经 <see cref="NodeExecutionContext"/>.Memory 暴露的字典，提供类型安全接口。
/// </summary>
public interface IWorkflowMemoryService
{
    /// <summary>读取共享状态中指定键的值，反序列化为 T。</summary>
    /// <param name="key">键。</param>
    /// <returns>反序列化的值；键不存在或值为 null 时返回 default。</returns>
    T? Get<T>(string key);

    /// <summary>写入共享状态中指定键的值（序列化为 JsonNode 存储）。</summary>
    /// <param name="key">键。</param>
    /// <param name="value">待写入的值。</param>
    void Set<T>(string key, T value);

    /// <summary>当前共享状态的快照（键 → JsonNode）。</summary>
    /// <returns>键值的只读快照枚举。</returns>
    IEnumerable<KeyValuePair<string, JsonNode?>> Snapshot();
}
