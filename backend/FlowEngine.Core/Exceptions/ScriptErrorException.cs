using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Exceptions;

/// <summary>
/// 脚本执行失败异常。
/// </summary>
public sealed class ScriptErrorException : Exception
{
    /// <summary>
    /// 失败脚本。
    /// </summary>
    public Script Script { get; }

    /// <summary>
    /// 失败原因摘要。
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// 初始化 <see cref="ScriptErrorException"/>。
    /// </summary>
    public ScriptErrorException(Script script, string reason, Exception? innerException = null)
        : base($"脚本执行失败: {reason} (source: {script.Source})", innerException)
    {
        Script = script;
        Reason = reason;
    }
}
