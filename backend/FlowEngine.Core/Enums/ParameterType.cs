using System.ComponentModel;

namespace FlowEngine.Core.Enums;

/// <summary>
/// 参数类型。
/// </summary>
public enum ParameterType
{
    /// <summary>
    /// 字符串。
    /// </summary>
    [Description("字符串")]
    String,

    /// <summary>
    /// 数字。
    /// </summary>
    [Description("数字")]
    Number,

    /// <summary>
    /// 布尔值。
    /// </summary>
    [Description("布尔值")]
    Boolean,

    /// <summary>
    /// 选项列表。
    /// </summary>
    [Description("选项")]
    Options,

    /// <summary>
    /// JSON。
    /// </summary>
    [Description("JSON")]
    Json,

    /// <summary>
    /// 代码。
    /// </summary>
    [Description("代码")]
    Code,

    /// <summary>
    /// 凭据。
    /// </summary>
    [Description("凭据")]
    Credential,

    /// <summary>
    /// 资源。
    /// </summary>
    [Description("资源")]
    Resource,

    /// <summary>
    /// 数组/列表。
    /// </summary>
    [Description("数组")]
    Array,

    /// <summary>
    /// 文件。
    /// </summary>
    [Description("文件")]
    File,

    /// <summary>
    /// 表达式。
    /// </summary>
    [Description("表达式")]
    Expression
}
