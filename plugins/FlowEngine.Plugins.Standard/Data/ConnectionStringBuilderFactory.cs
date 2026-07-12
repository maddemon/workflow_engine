using System.Collections.Generic;

namespace FlowEngine.Plugins.Standard.Data;

/// <summary>
/// 按方言获取对应的 <see cref="IConnectionStringBuilder"/>。
/// </summary>
public static class ConnectionStringBuilderFactory
{
    private static readonly Dictionary<DbDialect, IConnectionStringBuilder> Builders = new()
    {
        [DbDialect.PostgreSQL] = new PostgresConnectionStringBuilder(),
        [DbDialect.MySQL] = new MySqlConnectionStringBuilder(),
        [DbDialect.SqlServer] = new SqlServerConnectionStringBuilder(),
        [DbDialect.SQLite] = new SqliteConnectionStringBuilder(),
    };

    /// <summary>
    /// 获取指定方言的连接字符串生成器。
    /// </summary>
    public static IConnectionStringBuilder Get(DbDialect dialect) =>
        Builders.TryGetValue(dialect, out var builder)
            ? builder
            : throw new System.NotSupportedException($"不支持的方言：{dialect}");
}
