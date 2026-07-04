using System.ComponentModel;

namespace FlowEngine.Core.Authorization;

/// <summary>
/// 操作类型枚举。
/// </summary>
public enum Operation
{
    /// <summary>
    /// 读取。
    /// </summary>
    [Description("读取")]
    Read = 0,

    /// <summary>
    /// 写入。
    /// </summary>
    [Description("写入")]
    Write = 1,

    /// <summary>
    /// 执行。
    /// </summary>
    [Description("执行")]
    Execute = 2,

    /// <summary>
    /// 删除。
    /// </summary>
    [Description("删除")]
    Delete = 3
}