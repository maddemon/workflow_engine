using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 节点执行上下文工厂。
/// </summary>
public interface INodeExecutionContextFactory
{
    /// <summary>
    /// 创建节点执行上下文。
    /// </summary>
    /// <param name="extraGlobals">节点私有全局变量（如 PaginateNode 的 $cursor/$nextCursor/$page/$response），
    /// 由各自节点本地注入，工厂不感知具体变量名，避免顶层全局膨胀。</param>
    /// <param name="nodeContext">节点级持久化上下文（来自 <c>ExecutionSession.NodeContexts</c> 的同一实例）。
    /// 非 null 时注入运行时全局变量 <c>$nodeContext</c>，供节点 body 表达式读写。</param>
    Task<NodeExecutionContext> CreateAsync(
        Workflow workflow,
        ExecutionRecord execution,
        NodeDefinition node,
        INodeType nodeInstance,
        IReadOnlyDictionary<string, DataBatch> inputs,
        IReadOnlyDictionary<string, DataBatch> successfulOutputs,
        IReadOnlyDictionary<string, DataBatch> latestBatches,
        int runIndex,
        CancellationToken cancellationToken,
        ICredentialAccessor? credentialAccessorOverride = null,
        IReadOnlyDictionary<string, object?>? extraGlobals = null,
        IDictionary<string, object?>? nodeContext = null);
}
