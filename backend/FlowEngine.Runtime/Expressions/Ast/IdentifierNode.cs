namespace FlowEngine.Runtime.Expressions.Ast;

/// <summary>
/// 标识符节点。
/// </summary>
/// <param name="Name">标识符名称。</param>
public sealed record IdentifierNode(string Name) : ExpressionNode;
