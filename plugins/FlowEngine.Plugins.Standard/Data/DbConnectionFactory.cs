using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace FlowEngine.Plugins.Standard.Data;

/// <summary>
/// 按方言创建 <see cref="DbConnection"/>。
/// </summary>
public static class DbConnectionFactory
{
    /// <summary>
    /// 创建对应方言的数据库连接（未打开）。
    /// </summary>
    public static DbConnection CreateConnection(DbDialect dialect, string connectionString)
    {
        return dialect switch
        {
            DbDialect.PostgreSQL => new NpgsqlConnection(connectionString),
            DbDialect.MySQL => new MySqlConnection(connectionString),
            DbDialect.SqlServer => new SqlConnection(connectionString),
            DbDialect.SQLite => new SqliteConnection(connectionString),
            _ => throw new NotSupportedException($"不支持的方言：{dialect}")
        };
    }
}
