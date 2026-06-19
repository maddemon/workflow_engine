using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 端口实例。
/// </summary>
public class PortInstance
{
    /// <summary>
    /// 端口名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 端口方向。
    /// </summary>
    public PortDirection Direction { get; set; }

    /// <summary>
    /// 端口类型。
    /// </summary>
    public PortType Type { get; set; }
}
