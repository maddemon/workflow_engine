namespace FlowEngine.Core.Exceptions;

/// <summary>
/// 业务逻辑异常，映射为 HTTP 400 Bad Request。
/// 用于输入校验失败、业务规则违反等场景，替代 InvalidOperationException 的万能用法。
/// </summary>
public class BusinessException : DomainException
{
    public BusinessException() : base() { }
    public BusinessException(string message) : base(message) { }
    public BusinessException(string message, Exception innerException) : base(message, innerException) { }
}
