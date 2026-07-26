using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 子执行服务抽象：封装单次子节点执行，取代节点直接依赖
/// <see cref="INodeExecutionContextFactory"/> 的具体构造方式，便于 Phase 4 节点迁移与测试。
/// </summary>
public interface ISubExecutionService
{
    /// <summary>在给定上下文下执行单个子节点，返回其执行结果。</summary>
    /// <param name="workflow">所属工作流定义。</param>
    /// <param name="execution">执行记录。</param>
    /// <param name="node">子节点定义。</param>
    /// <param name="nodeType">子节点类型实例。</param>
    /// <param name="inputs">按端口组织的输入批。</param>
    /// <param name="runIndex">运行索引。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>子节点执行结果。</returns>
    Task<NodeExecutionResult> ExecuteSubAsync(
        Workflow workflow,
        ExecutionRecord execution,
        NodeDefinition node,
        INodeType nodeType,
        IReadOnlyDictionary<string, DataBatch> inputs,
        int runIndex,
        CancellationToken ct = default);

    /// <summary>扫描 Agent 工具端口连接，返回工具定义列表（取代节点直接读取 context.Workflow/Node/NodeRegistry）。</summary>
    /// <param name="context">当前节点执行上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>连接至本节点 Tools 端口的工具节点定义列表；无连接时返回空列表。</returns>
    Task<IReadOnlyList<ToolDefinition>> ResolveAgentToolsAsync(NodeExecutionContext context, CancellationToken ct = default);
}
