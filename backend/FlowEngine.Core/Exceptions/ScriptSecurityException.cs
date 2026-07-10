using FlowEngine.Core.Scripting;

namespace FlowEngine.Core.Exceptions;

/// <summary>
/// 脚本包含被禁止的标识符或违反安全策略时抛出。
/// </summary>
public sealed class ScriptSecurityException : ScriptErrorException
{
    /// <summary>
    /// 初始化 <see cref="ScriptSecurityException"/>。
    /// </summary>
    public ScriptSecurityException(Script script, string identifier)
        : base(script, $"脚本包含禁止使用的标识符 '{identifier}'")
    {
        Identifier = identifier;
    }

    /// <summary>
    /// 被禁止的标识符。
    /// </summary>
    public string Identifier { get; }
}
