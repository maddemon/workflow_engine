using System.Collections.Generic;
using System.Data.Common;
using MySqlConnector;

namespace FlowEngine.Plugins.Standard.Data;

/// <summary>
/// MySQL 连接字符串生成器。直接使用 MySqlConnector 自带的 <see cref="MySqlConnector.MySqlConnectionStringBuilder"/>，
/// 通过其基类索引器按字段赋值（标准关键字 Server/Port/Database/User ID/Password/SslMode 均经索引器暴露）。
/// 注意：本类名与提供程序类名相同，故使用 <see cref="MySqlConnector.MySqlConnectionStringBuilder"/> 全名以避免歧义。
/// 读取字段：host、port、database、userid、password、ssl。
/// </summary>
public sealed class MySqlConnectionStringBuilder : IConnectionStringBuilder
{
    /// <inheritdoc />
    public DbDialect Dialect => DbDialect.MySQL;

    /// <inheritdoc />
    public string Build(IReadOnlyDictionary<string, string> fields)
    {
        var builder = new MySqlConnector.MySqlConnectionStringBuilder();
        var db = (DbConnectionStringBuilder)builder;

        db["Server"] = RequireField(fields, "host");

        if (TryGetInt(fields, "port", out var port))
        {
            db["Port"] = port;
        }

        SetIfPresent(db, fields, "Database", "database");
        SetIfPresent(db, fields, "User ID", "userid", "username");
        SetIfPresent(db, fields, "Password", "password");
        SetIfPresent(db, fields, "SslMode", "ssl");

        return builder.ConnectionString;
    }

    private static string RequireField(IReadOnlyDictionary<string, string> fields, string name)
    {
        if (!fields.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new System.InvalidOperationException($"凭据缺少必填字段 '{name}'。");
        }

        return value!;
    }

    private static void SetIfPresent(
        DbConnectionStringBuilder builder,
        IReadOnlyDictionary<string, string> fields,
        string key,
        params string[] fieldNames)
    {
        foreach (var fieldName in fieldNames)
        {
            if (fields.TryGetValue(fieldName, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                builder[key] = value!;
                return;
            }
        }
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, string> fields, string name, out int result)
    {
        result = 0;
        return fields.TryGetValue(name, out var value)
            && !string.IsNullOrWhiteSpace(value)
            && int.TryParse(value, out result);
    }
}
