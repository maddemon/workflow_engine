using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 节点执行上下文工厂接口，供需要在运行时创建子节点上下文的节点使用。
/// </summary>
public interface INodeExecutionContextFactory
{
    /// <summary>
    /// 为指定节点实例创建执行上下文。
    /// </summary>
    Task<NodeExecutionContext> CreateAsync(
        Workflow workflow,
        ExecutionRecord execution,
        NodeDefinition node,
        INodeType nodeInstance,
        IReadOnlyDictionary<string, DataBatch> inputs,
        IReadOnlyDictionary<string, DataBatch> successfulOutputs,
        IReadOnlyDictionary<string, DataBatch> latestBatches,
        int runIndex,
        CancellationToken cancellationToken);
}
