using FlowEngine.Core.Exceptions;

namespace FlowEngine.Core.Exceptions;
/// <summary>节点业务执行异常。节点业务逻辑失败时应抛出此异常，由框架（如 <c>NodeBase</c>）捕获并
/// 统一转换为 <see cref="NodeExecutionResult"/> 的 <see cref="NodeError"/>。</summary>
public sealed class NodeExecutionException : DomainException
{
    /// <summary>错误码，对应 <see cref="NodeError.Code"/>。</summary>
    public string ErrorCode { get; }

    /// <summary>构造节点执行异常。</summary>
    /// <param name="errorCode">错误码。</param>
    /// <param name="message">错误描述。</param>
    public NodeExecutionException(string errorCode, string message) : base(message) => ErrorCode = errorCode;
}
