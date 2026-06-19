namespace FlowEngine.Runtime.Expressions.Ast;

/// <summary>
/// 索引器节点。
/// </summary>
/// <param name="Target">目标表达式。</param>
/// <param name="Index">索引表达式。</param>
public sealed record IndexerNode(ExpressionNode Target, ExpressionNode Index) : ExpressionNode;
