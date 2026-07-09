namespace FlowEngine.Core.Exceptions;

/// <summary>
/// 未认证异常，对应当前用户未登录，映射 HTTP 401 Unauthorized。
/// </summary>
public sealed class UnauthorizedException : UnauthorizedAccessException
{
    public UnauthorizedException(string message)
        : base(message)
    {
    }
}
