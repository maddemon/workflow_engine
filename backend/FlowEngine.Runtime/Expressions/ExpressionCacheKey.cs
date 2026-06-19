namespace FlowEngine.Runtime.Expressions;

/// <summary>
/// 表达式 AST 缓存键。
/// </summary>
/// <param name="Expression">原始表达式文本。</param>
/// <param name="InputSchemaHash">输入 Schema 哈希。</param>
/// <param name="ParameterSchemaHash">参数 Schema 哈希。</param>
public sealed record ExpressionCacheKey(
    string Expression,
    string InputSchemaHash,
    string ParameterSchemaHash);
