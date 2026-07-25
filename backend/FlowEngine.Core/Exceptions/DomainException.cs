namespace FlowEngine.Core.Exceptions;

/// <summary>
/// 领域异常基类。所有业务/领域异常继承自此类型，统一异常中间件按基类映射为 400，
/// 同时保留具体子类型的精确映射（如 <see cref="NotFoundException"/> → 404）。
/// </summary>
public class DomainException : Exception
{
    public DomainException() : base() { }
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}
