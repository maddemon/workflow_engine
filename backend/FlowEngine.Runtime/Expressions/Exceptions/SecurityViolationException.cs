namespace FlowEngine.Runtime.Expressions.Exceptions;

/// <summary>
/// 表达式尝试访问禁止资源或函数。
/// </summary>
public sealed class SecurityViolationException : ExpressionEvaluationException
{
    /// <summary>
    /// 初始化异常。
    /// </summary>
    public SecurityViolationException(
        string expression,
        string reason,
        Exception? innerException = null)
        : base(expression, reason, null, innerException)
    {
    }
}
