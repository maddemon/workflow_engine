namespace FlowEngine.Runtime.Expressions.Ast;

/// <summary>
/// 二元运算符。
/// </summary>
public enum BinaryOperator
{
    /// <summary>
    /// 加法。
    /// </summary>
    Add,

    /// <summary>
    /// 减法。
    /// </summary>
    Subtract,

    /// <summary>
    /// 乘法。
    /// </summary>
    Multiply,

    /// <summary>
    /// 除法。
    /// </summary>
    Divide,

    /// <summary>
    /// 取模。
    /// </summary>
    Modulo,

    /// <summary>
    /// 等于。
    /// </summary>
    Equal,

    /// <summary>
    /// 不等于。
    /// </summary>
    NotEqual,

    /// <summary>
    /// 大于。
    /// </summary>
    GreaterThan,

    /// <summary>
    /// 小于。
    /// </summary>
    LessThan,

    /// <summary>
    /// 大于等于。
    /// </summary>
    GreaterThanOrEqual,

    /// <summary>
    /// 小于等于。
    /// </summary>
    LessThanOrEqual,

    /// <summary>
    /// 逻辑与。
    /// </summary>
    And,

    /// <summary>
    /// 逻辑或。
    /// </summary>
    Or
}
