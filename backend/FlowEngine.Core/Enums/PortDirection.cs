using System.ComponentModel;

namespace FlowEngine.Core.Enums;

/// <summary>
/// 端口方向。
/// </summary>
public enum PortDirection
{
    /// <summary>
    /// 输入端口。
    /// </summary>
    [Description("输入")]
    Input,

    /// <summary>
    /// 输出端口。
    /// </summary>
    [Description("输出")]
    Output
}
