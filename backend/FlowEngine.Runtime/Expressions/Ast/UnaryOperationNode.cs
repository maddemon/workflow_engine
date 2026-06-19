namespace FlowEngine.Runtime.Expressions.Ast;

/// <summary>
/// 一元运算节点。
/// </summary>
/// <param name="Operator">运算符。</param>
/// <param name="Operand">操作数。</param>
public sealed record UnaryOperationNode(UnaryOperator Operator, ExpressionNode Operand) : ExpressionNode;
