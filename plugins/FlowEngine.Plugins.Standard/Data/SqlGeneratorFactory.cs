namespace FlowEngine.Plugins.Standard.Data;

/// <summary>
/// 获取方言对应的 SQL 生成器。
/// </summary>
public static class SqlGeneratorFactory
{
    /// <summary>
    /// 创建 SQL 生成器。
    /// </summary>
    public static IDbSqlGenerator Create(DbDialect dialect)
    {
        return dialect switch
        {
            DbDialect.PostgreSQL => new PostgresSqlGenerator(),
            DbDialect.MySQL => new MySqlSqlGenerator(),
            DbDialect.SqlServer => new SqlServerSqlGenerator(),
            DbDialect.SQLite => new SQLiteSqlGenerator(),
            _ => throw new NotSupportedException($"不支持的方言：{dialect}")
        };
    }
}
