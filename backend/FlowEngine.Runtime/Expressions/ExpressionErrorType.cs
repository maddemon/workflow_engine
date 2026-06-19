namespace FlowEngine.Runtime.Expressions;

/// <summary>
/// 表达式求值错误类型。
/// </summary>
public enum ExpressionErrorType
{
    /// <summary>
    /// 语法错误。
    /// </summary>
    SyntaxError,

    /// <summary>
    /// 字段不存在。
    /// </summary>
    FieldNotFound,

    /// <summary>
    /// 节点输出不存在。
    /// </summary>
    NodeOutputNotFound,

    /// <summary>
    /// 类型不匹配。
    /// </summary>
    TypeMismatch,

    /// <summary>
    /// 安全违规。
    /// </summary>
    SecurityViolation
}
