using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 节点类型契约，定义节点的元数据与执行行为。
/// </summary>
public interface INodeType
{
    /// <summary>
    /// 节点类型唯一标识。
    /// </summary>
    string TypeName { get; }

    /// <summary>
    /// 显示名称。
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// 节点分类。
    /// </summary>
    string Category { get; }

    /// <summary>
    /// 节点图标。
    /// </summary>
    string Icon { get; }

    /// <summary>
    /// 节点执行模式。
    /// </summary>
    ExecutionMode ExecutionMode { get; }

    /// <summary>
    /// 端口定义列表。
    /// </summary>
    IReadOnlyList<PortDefinition> Ports { get; }

    /// <summary>
    /// 是否默认作为入口节点。
    /// </summary>
    bool DefaultIsEntry { get; }

    /// <summary>
    /// 执行节点逻辑。
    /// </summary>
    /// <param name="context">节点执行上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>节点执行结果。</returns>
    Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default);
}
