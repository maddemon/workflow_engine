namespace FlowEngine.Core.Exceptions;

/// <summary>
/// 权限不足异常，对应 HTTP 403 Forbidden。
/// </summary>
public sealed class PermissionDeniedException : UnauthorizedAccessException
{
    public PermissionDeniedException(string message)
        : base(message)
    {
    }
}
