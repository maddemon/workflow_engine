namespace FlowEngine.Runtime.Expressions.Exceptions;

/// <summary>
/// 表达式引用了不存在的字段。
/// </summary>
public sealed class FieldNotFoundException : ExpressionEvaluationException
{
    /// <summary>
    /// 缺失的字段名。
    /// </summary>
    public string FieldName { get; }

    /// <summary>
    /// 初始化异常。
    /// </summary>
    public FieldNotFoundException(
        string expression,
        string fieldName,
        IEnumerable<string>? availableFields = null,
        Exception? innerException = null)
        : base(expression, $"字段 '{fieldName}' 不存在", availableFields, innerException)
    {
        FieldName = fieldName;
    }
}
