namespace FlowEngine.Runtime.Expressions.Ast;

/// <summary>
/// 成员访问节点。
/// </summary>
/// <param name="Target">目标表达式。</param>
/// <param name="MemberName">成员名称。</param>
public sealed record MemberAccessNode(ExpressionNode Target, string MemberName) : ExpressionNode;
