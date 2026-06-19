namespace FlowEngine.Runtime.Expressions.Ast;

/// <summary>
/// 字面量节点。
/// </summary>
/// <param name="Value">字面量值。</param>
public sealed record LiteralNode(object? Value) : ExpressionNode;
