namespace FlowEngine.Core.Exceptions;

/// <summary>
/// 权限不足异常，映射为 HTTP 403 Forbidden。
/// </summary>
public class PermissionDeniedException : Exception
{
    public PermissionDeniedException() : base("权限不足。") { }
    public PermissionDeniedException(string message) : base(message) { }
    public PermissionDeniedException(string message, Exception innerException) : base(message, innerException) { }
}
