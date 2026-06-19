namespace FlowEngine.Runtime.Expressions;

/// <summary>
/// 表达式求值错误信息。
/// </summary>
public sealed class ExpressionError
{
    /// <summary>
    /// 错误类型。
    /// </summary>
    public ExpressionErrorType Type { get; init; }

    /// <summary>
    /// 原始表达式文本。
    /// </summary>
    public string Expression { get; init; } = string.Empty;

    /// <summary>
    /// 错误原因。
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// 错误位置（从 0 开始），-1 表示未知。
    /// </summary>
    public int Position { get; init; } = -1;

    /// <summary>
    /// 当前可用的字段列表。
    /// </summary>
    public IReadOnlyList<string> AvailableFields { get; init; } = [];
}
