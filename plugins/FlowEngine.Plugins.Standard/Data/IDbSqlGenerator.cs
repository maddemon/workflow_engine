namespace FlowEngine.Plugins.Standard.Data;

/// <summary>
/// 方言 SQL 生成器接口。
/// </summary>
public interface IDbSqlGenerator
{
    /// <summary>
    /// 生成 upsert SQL（参数占位符为 @p0..@pN，按 <paramref name="columns"/> 顺序）。
    /// </summary>
    string BuildUpsertSql(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns);

    /// <summary>
    /// 生成 insert SQL。
    /// </summary>
    string BuildInsertSql(string table, IReadOnlyList<string> columns);

    /// <summary>
    /// 生成 update SQL（set 参数在前，key 参数在后）。
    /// </summary>
    string BuildUpdateSql(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns);

    /// <summary>
    /// 按方言引用标识符。
    /// </summary>
    string QuoteIdentifier(string identifier);
}
