using FlowEngine.Core.Exceptions;

namespace FlowEngine.Core.Exceptions;
/// <summary>节点参数解析异常。当已求值参数（如 <see cref="Script"/> 的 ResolvedValue）缺失或无法转换为目标类型时抛出，
/// 由 <see cref="Scripting.ScriptResolvedValueExtensions.GetResolved{T}"/> 等使用。</summary>
public sealed class NodeParameterException : DomainException
{
    /// <summary>构造节点参数异常，描述参数名与期望类型。</summary>
    /// <param name="parameterName">参数名称。</param>
    /// <param name="expectedType">期望解析的目标类型。</param>
    public NodeParameterException(string parameterName, Type expectedType)
        : base($"参数 '{parameterName}' 无法解析为类型 {expectedType.Name}。")
    {
    }

    /// <summary>构造节点参数异常，使用自定义消息。</summary>
    /// <param name="message">错误描述。</param>
    public NodeParameterException(string message) : base(message)
    {
    }
}
