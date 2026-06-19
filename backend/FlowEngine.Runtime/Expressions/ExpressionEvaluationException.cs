namespace FlowEngine.Runtime.Expressions;

/// <summary>
/// 表达式求值异常。
/// </summary>
public class ExpressionEvaluationException : Exception
{
    /// <summary>
    /// 错误详情。
    /// </summary>
    public ExpressionError Error { get; }

    /// <summary>
    /// 初始化 <see cref="ExpressionEvaluationException"/>。
    /// </summary>
    /// <param name="error">错误详情。</param>
    public ExpressionEvaluationException(ExpressionError error)
        : base(error.Reason)
    {
        Error = error;
    }
}
