using System.Collections.Generic;

namespace FlowEngine.Core.Abstractions;
/// <summary>节点级持久化状态占位类型（跨计划共用）。当前为 IDictionary&lt;string,object?&gt; 的轻量包装，
/// 待 plan-node-level-context-architecture 落地后由更完整的实现替换。请勿在管线共享上下文中内嵌此类型。</summary>
public sealed class NodeContext
{
    /// <summary>节点级状态字典（键不区分大小写）。</summary>
    public IDictionary<string, object?> State { get; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>使用空状态初始化 <see cref="NodeContext"/>。</summary>
    public NodeContext() { }

    /// <summary>使用给定的状态字典初始化 <see cref="NodeContext"/>。</summary>
    /// <param name="state">节点级状态字典。</param>
    public NodeContext(IDictionary<string, object?> state) => State = state;
}
