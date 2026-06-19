namespace FlowEngine.Runtime.Expressions.Ast;

/// <summary>
/// 表达式片段。
/// </summary>
/// <param name="Expression">表达式 AST 节点。</param>
public sealed record ExpressionSegment(ExpressionNode Expression) : Segment;
