using System.ComponentModel;

namespace FlowEngine.Plugins.Standard.Enums;

/// <summary>
/// 游标类型。
/// </summary>
public enum CursorType
{
    /// <summary>
    /// 数字游标。
    /// </summary>
    [Description("数字")]
    Number = 0,

    /// <summary>
    /// 字符串游标。
    /// </summary>
    [Description("字符串")]
    String = 1
}
