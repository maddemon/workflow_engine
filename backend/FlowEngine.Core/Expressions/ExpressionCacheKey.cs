namespace FlowEngine.Core.Expressions;

/// <summary>
/// 表达式 AST 缓存键。同一表达式在不同输入/参数 schema 下会分别缓存，
/// schema 变化后自动失效。
/// </summary>
public sealed record ExpressionCacheKey(
    string Expression,
    string InputSchemaHash,
    string ParameterSchemaHash);
