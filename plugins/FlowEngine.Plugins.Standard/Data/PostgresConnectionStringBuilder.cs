using System.Collections.Generic;
using System.Data.Common;
using Npgsql;

namespace FlowEngine.Plugins.Standard.Data;

/// <summary>
/// PostgreSQL 连接字符串生成器。直接使用 Npgsql 自带的 <see cref="NpgsqlConnectionStringBuilder"/>，
/// 通过其基类索引器按字段赋值（Npgsql 会校验并规范化关键字，例如 SslMode 可直接接受 "require" 等字符串）。
/// 读取字段：host、port、database、userid、password、ssl。
/// </summary>
public sealed class PostgresConnectionStringBuilder : IConnectionStringBuilder
{
    /// <inheritdoc />
    public DbDialect Dialect => DbDialect.PostgreSQL;

    /// <inheritdoc />
    public string Build(IReadOnlyDictionary<string, string> fields)
    {
        var builder = new NpgsqlConnectionStringBuilder();
        var db = (DbConnectionStringBuilder)builder;

        db["Host"] = RequireField(fields, "host");

        if (TryGetInt(fields, "port", out var port))
        {
            db["Port"] = port;
        }

        SetIfPresent(db, fields, "Database", "database");
        SetIfPresent(db, fields, "Username", "userid", "username");
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
