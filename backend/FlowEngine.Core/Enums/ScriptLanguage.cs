using System.ComponentModel;

namespace FlowEngine.Core.Enums;

/// <summary>
/// 脚本语言类型。
/// </summary>
public enum ScriptLanguage
{
    /// <summary>
    /// JavaScript（通过 Jint 执行）。
    /// </summary>
    [Description("JavaScript")]
    JavaScript,

    /// <summary>
    /// Python（预留，暂不支持）。
    /// </summary>
    [Description("Python")]
    Python
}
