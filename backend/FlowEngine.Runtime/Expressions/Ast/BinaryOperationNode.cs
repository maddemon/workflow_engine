namespace FlowEngine.Runtime.Expressions.Ast;

/// <summary>
/// 二元运算节点。
/// </summary>
/// <param name="Operator">运算符。</param>
/// <param name="Left">左操作数。</param>
/// <param name="Right">右操作数。</param>
public sealed record BinaryOperationNode(BinaryOperator Operator, ExpressionNode Left, ExpressionNode Right) : ExpressionNode;
