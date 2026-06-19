using System.ComponentModel;

namespace FlowEngine.Core.Enums;

/// <summary>
/// 参数渲染提示，指导前端使用何种组件渲染字段。
/// 节点作者可显式指定；未指定时由前端按 <see cref="ParameterType"/> 自动推断。
/// </summary>
public enum PresentationHint
{
    /// <summary>
    /// 按参数类型默认渲染。
    /// </summary>
    [Description("默认")]
    Default,

    /// <summary>
    /// 横向按钮组，用于少量互斥选项（如 HTTP method）。
    /// </summary>
    [Description("按钮组")]
    ButtonGroup,

    /// <summary>
    /// 下拉选择框，用于较多选项（5 项以上）。
    /// </summary>
    [Description("下拉选择")]
    Select,

    /// <summary>
    /// 多行文本。
    /// </summary>
    [Description("多行文本")]
    TextArea,

    /// <summary>
    /// 代码编辑器。
    /// </summary>
    [Description("代码编辑器")]
    CodeEditor,

    /// <summary>
    /// JSON 编辑器。
    /// </summary>
    [Description("JSON 编辑器")]
    JsonEditor,

    /// <summary>
    /// 键值对编辑器，用于编辑键值对列表（如 HTTP Headers）。
    /// </summary>
    [Description("键值对编辑器")]
    KeyValueEditor,

    /// <summary>
    /// 开关样式布尔值。
    /// </summary>
    [Description("开关")]
    Toggle,

    /// <summary>
    /// 密码型输入。
    /// </summary>
    [Description("密码输入")]
    Secret,

    /// <summary>
    /// 凭据下拉。
    /// </summary>
    [Description("凭据选择")]
    CredentialSelect,

    /// <summary>
    /// 动态资源选择。
    /// </summary>
    [Description("资源选择")]
    ResourceSelect,

    /// <summary>
    /// 文件上传。
    /// </summary>
    [Description("文件上传")]
    FileUpload,

    /// <summary>
    /// 表达式输入。
    /// </summary>
    [Description("表达式")]
    Expression,

    /// <summary>
    /// 可增删行的列表。
    /// </summary>
    [Description("列表")]
    Array,

    /// <summary>
    /// 日期时间选择器。
    /// </summary>
    [Description("日期时间")]
    DateTime
}
