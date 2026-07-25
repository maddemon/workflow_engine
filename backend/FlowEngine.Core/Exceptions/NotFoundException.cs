namespace FlowEngine.Core.Exceptions;

/// <summary>
/// 资源未找到异常，映射为 HTTP 404 Not Found。
/// </summary>
public class NotFoundException : DomainException
{
    public NotFoundException() : base("资源不存在。") { }
    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string message, Exception innerException) : base(message, innerException) { }
}
