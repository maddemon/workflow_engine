namespace FlowEngine.Runtime.Expressions.Exceptions;

/// <summary>
/// 表达式求值异常的基类。
/// </summary>
public abstract class ExpressionEvaluationException : Exception
{
    /// <summary>
    /// 原始表达式文本。
    /// </summary>
    public string Expression { get; }

    /// <summary>
    /// 失败原因（已本地化）。
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// 当前上下文中可用的字段列表（可选）。
    /// </summary>
    public IReadOnlyList<string> AvailableFields { get; }

    /// <summary>
    /// 初始化异常。
    /// </summary>
    protected ExpressionEvaluationException(
        string expression,
        string reason,
        IEnumerable<string>? availableFields = null,
        Exception? innerException = null)
        : base($"表达式求值失败: {reason} (expression: {expression})", innerException)
    {
        Expression = expression;
        Reason = reason;
        AvailableFields = availableFields?.ToList() ?? [];
    }
}
