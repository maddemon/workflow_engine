using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Attributes;
/// <summary>声明节点类型元数据。配合 <see cref="PortAttribute"/> 使用，供 <c>NodeBase</c> 反射派生
/// <see cref="FlowEngine.Core.Abstractions.INodeType"/> 的元信息。仅作用于类，须使用命名参数赋值。</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NodeMetaAttribute : Attribute
{
    /// <summary>节点类型唯一标识。</summary>
    public required string TypeName { get; init; }

    /// <summary>显示名称。</summary>
    public required string DisplayName { get; init; }

    /// <summary>节点分类。</summary>
    public required NodeCategory Category { get; init; }

    /// <summary>节点图标。</summary>
    public required string Icon { get; init; }

    /// <summary>是否默认作为入口节点。</summary>
    public bool DefaultIsEntry { get; init; } = false;
}
