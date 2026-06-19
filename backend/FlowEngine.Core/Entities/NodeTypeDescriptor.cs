using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 节点类型元数据描述，用于缓存和 API 返回。
/// </summary>
public sealed class NodeTypeDescriptor
{
    /// <summary>
    /// 节点类型唯一标识。
    /// </summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// 节点分类。
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// 节点图标。
    /// </summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>
    /// 执行模式。
    /// </summary>
    public ExecutionMode ExecutionMode { get; init; }

    /// <summary>
    /// 参数定义列表。
    /// </summary>
    public IReadOnlyList<ParameterDefinition> Parameters { get; init; } = [];

    /// <summary>
    /// 端口定义列表。
    /// </summary>
    public IReadOnlyList<PortDefinition> Ports { get; init; } = [];

    /// <summary>
    /// 是否默认作为入口节点。
    /// </summary>
    public bool DefaultIsEntry { get; init; }
}
