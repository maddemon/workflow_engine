using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 端口定义。
/// </summary>
public class PortDefinition
{
    /// <summary>
    /// 端口名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 端口方向。
    /// </summary>
    public PortDirection Direction { get; set; }

    /// <summary>
    /// 端口类型。
    /// </summary>
    public PortType Type { get; set; }

    /// <summary>
    /// 是否必填。
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// 端口条件表达式，用于 Switch 等分支节点。
    /// 值为 "*" 表示默认分支。
    /// </summary>
    public string? Condition { get; set; }

    /// <summary>
    /// 允许的数据类型列表。
    /// </summary>
    public List<string> AllowedTypes { get; set; } = [];

    /// <summary>
    /// 输出数据模式。
    /// </summary>
    public DataSchema? OutputSchema { get; set; }

    /// <summary>
    /// 期望输入数据模式。
    /// </summary>
    public DataSchema? ExpectedSchema { get; set; }
}
