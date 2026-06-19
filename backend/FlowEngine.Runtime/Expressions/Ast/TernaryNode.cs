namespace FlowEngine.Runtime.Expressions.Ast;

/// <summary>
/// 条件表达式节点。
/// </summary>
/// <param name="Condition">条件表达式。</param>
/// <param name="TrueExpression">条件为真时的表达式。</param>
/// <param name="FalseExpression">条件为假时的表达式。</param>
public sealed record TernaryNode(ExpressionNode Condition, ExpressionNode TrueExpression, ExpressionNode FalseExpression) : ExpressionNode;
