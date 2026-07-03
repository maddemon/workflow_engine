namespace FlowEngine.Runtime.Expressions.Exceptions;

/// <summary>
/// 表达式存在语法错误。
/// </summary>
public sealed class SyntaxErrorException : ExpressionEvaluationException
{
    /// <summary>
    /// 初始化异常。
    /// </summary>
    public SyntaxErrorException(
        string expression,
        string reason,
        Exception? innerException = null)
        : base(expression, reason, null, innerException)
    {
    }
}
