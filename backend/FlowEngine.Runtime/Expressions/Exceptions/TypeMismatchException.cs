namespace FlowEngine.Runtime.Expressions.Exceptions;

/// <summary>
/// 表达式运算类型不匹配。
/// </summary>
public sealed class TypeMismatchException : ExpressionEvaluationException
{
    /// <summary>
    /// 初始化异常。
    /// </summary>
    public TypeMismatchException(
        string expression,
        string reason,
        Exception? innerException = null)
        : base(expression, reason, null, innerException)
    {
    }
}
