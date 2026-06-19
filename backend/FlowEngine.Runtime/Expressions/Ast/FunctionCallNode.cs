namespace FlowEngine.Runtime.Expressions.Ast;

/// <summary>
/// 函数调用节点。
/// </summary>
/// <param name="FunctionName">函数名称。</param>
/// <param name="Arguments">参数列表。</param>
public sealed record FunctionCallNode(string FunctionName, IReadOnlyList<ExpressionNode> Arguments) : ExpressionNode;
