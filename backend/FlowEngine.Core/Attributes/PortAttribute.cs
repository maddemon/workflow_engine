using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Attributes;
/// <summary>声明节点的端口定义。可多次应用于同一类以声明多个端口。供 <c>NodeBase</c> 反射派生端口列表。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class PortAttribute : Attribute
{
    /// <summary>端口名称。</summary>
    public string Name { get; }

    /// <summary>端口显示名称。</summary>
    public string DisplayName { get; }

    /// <summary>端口方向（输入/输出）。</summary>
    public PortDirection Direction { get; }

    /// <summary>端口类型，默认 <see cref="PortType.Main"/>。</summary>
    public PortType Type { get; }

    /// <summary>构造端口特性。</summary>
    /// <param name="name">端口名称。</param>
    /// <param name="displayName">端口显示名称。</param>
    /// <param name="direction">端口方向。</param>
    /// <param name="type">端口类型。</param>
    public PortAttribute(string name, string displayName, PortDirection direction, PortType type = PortType.Main)
        => (Name, DisplayName, Direction, Type) = (name, displayName, direction, type);
}
