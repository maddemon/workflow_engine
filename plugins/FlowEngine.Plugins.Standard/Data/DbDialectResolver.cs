namespace FlowEngine.Plugins.Standard.Data;

/// <summary>
/// 从凭据的 dbType 字段解析数据库方言。
/// </summary>
public static class DbDialectResolver
{
    /// <summary>
    /// 将凭据的 <c>dbType</c> 字段解析为方言。
    /// 接受：postgresql/postgres、mysql、sqlserver/mssql、sqlite。
    /// </summary>
    /// <param name="dbType">凭据的 dbType 值。</param>
    /// <returns>解析出的方言。</returns>
    /// <exception cref="System.InvalidOperationException">dbType 为空或无法识别时抛出。</exception>
    public static DbDialect ParseDbType(string? dbType)
    {
        if (string.IsNullOrWhiteSpace(dbType))
        {
            throw new System.InvalidOperationException("凭据缺少 dbType 字段，无法确定数据库方言。");
        }

        if (System.Enum.TryParse<DbDialect>(dbType, true, out var dialect))
        {
            return dialect;
        }

        // 兼容常见别名
        var normalized = dbType.Trim().ToLowerInvariant();
        return normalized switch
        {
            "postgres" => DbDialect.PostgreSQL,
            "mssql" => DbDialect.SqlServer,
            _ => throw new System.InvalidOperationException($"无法识别的 dbType：'{dbType}'。可用值：postgresql, mysql, sqlserver, sqlite。")
        };
    }
}
