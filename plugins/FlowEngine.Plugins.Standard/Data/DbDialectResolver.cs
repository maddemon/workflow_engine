namespace FlowEngine.Plugins.Standard.Data;

/// <summary>
/// 从显式方言或连接字符串推断数据库方言。
/// </summary>
public static class DbDialectResolver
{
    /// <summary>
    /// 解析方言。
    /// </summary>
    /// <param name="dialect">显式方言名称（可选）。</param>
    /// <param name="connectionString">数据库连接字符串。</param>
    /// <returns>推断出的方言。</returns>
    public static DbDialect Resolve(string? dialect, string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(dialect))
        {
            if (Enum.TryParse<DbDialect>(dialect, true, out var explicitDialect))
            {
                return explicitDialect;
            }

            throw new NotSupportedException($"不支持的显式方言：'{dialect}'。");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("连接字符串不能为空。", nameof(connectionString));
        }

        var cs = connectionString;

        // PostgreSQL 通常使用 Host=；MySQL/SQL Server 通常使用 Server=
        if (ContainsKey(cs, "Host="))
        {
            return DbDialect.PostgreSQL;
        }

        if (ContainsKey(cs, "Server="))
        {
            // SQL Server 常用 Initial Catalog / Trusted_Connection / Encrypt / Integrated Security
            if (ContainsKey(cs, "Initial Catalog=") ||
                ContainsKey(cs, "Trusted_Connection=") ||
                ContainsKey(cs, "Encrypt=") ||
                ContainsKey(cs, "Integrated Security="))
            {
                return DbDialect.SqlServer;
            }

            return DbDialect.MySQL;
        }

        if (ContainsKey(cs, "Data Source="))
        {
            return DbDialect.SQLite;
        }

        throw new NotSupportedException("无法从连接字符串推断数据库方言。");
    }

    private static bool ContainsKey(string connectionString, string key)
    {
        return connectionString.Contains(key, StringComparison.OrdinalIgnoreCase);
    }
}
