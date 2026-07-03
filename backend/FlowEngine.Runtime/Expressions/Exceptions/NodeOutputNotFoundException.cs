namespace FlowEngine.Runtime.Expressions.Exceptions;

/// <summary>
/// 表达式引用了不存在的节点输出。
/// </summary>
public sealed class NodeOutputNotFoundException : ExpressionEvaluationException
{
    /// <summary>
    /// 缺失的节点名称。
    /// </summary>
    public string NodeName { get; }

    /// <summary>
    /// 初始化异常。
    /// </summary>
    public NodeOutputNotFoundException(
        string expression,
        string nodeName,
        Exception? innerException = null)
        : base(expression, $"节点 '{nodeName}' 的输出不存在", null, innerException)
    {
        NodeName = nodeName;
    }
}
