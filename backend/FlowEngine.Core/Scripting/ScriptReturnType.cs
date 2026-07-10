namespace FlowEngine.Core.Scripting;

/// <summary>
/// 脚本结果返回类型提示。
/// </summary>
public enum ScriptReturnType
{
    /// <summary>
    /// 字符串。
    /// </summary>
    String,

    /// <summary>
    /// 对象（JSON 节点或 CLR 对象）。
    /// </summary>
    Object,

    /// <summary>
    /// 布尔值。
    /// </summary>
    Bool,

    /// <summary>
    /// 数值。
    /// </summary>
    Number,

    /// <summary>
    /// 字典（键值对映射）。
    /// </summary>
    Dictionary,
}
