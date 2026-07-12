using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;

namespace FlowEngine.Plugins.Standard.Data;

/// <summary>
/// SQL Server 连接字符串生成器。直接使用 Microsoft.Data.SqlClient 自带的 <see cref="SqlConnectionStringBuilder"/> 组装。
/// host 与 port 合并写入 DataSource（格式 host 或 host,port）；
/// ssl 字段映射为 Encrypt（解析为布尔）。
/// 读取字段：host、port、database、userid、password、ssl。
/// </summary>
public sealed class SqlServerConnectionStringBuilder : IConnectionStringBuilder
{
    /// <inheritdoc />
    public DbDialect Dialect => DbDialect.SqlServer;

    /// <inheritdoc />
    public string Build(IReadOnlyDictionary<string, string> fields)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = TryGetInt(fields, "port", out var port)
                ? $"{RequireField(fields, "host")},{port}"
                : RequireField(fields, "host")
        };

        SetIfPresent(builder, fields, v => builder.InitialCatalog = v, "database");
        SetIfPresent(builder, fields, v => builder.UserID = v, "userid", "username");
        SetIfPresent(builder, fields, v => builder.Password = v, "password");

        if (fields.TryGetValue("ssl", out var ssl) && !string.IsNullOrWhiteSpace(ssl) && bool.TryParse(ssl, out var encrypt))
        {
            builder.Encrypt = encrypt;
        }

        return builder.ConnectionString;
    }

    private static string RequireField(IReadOnlyDictionary<string, string> fields, string name)
    {
        if (!fields.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"凭据缺少必填字段 '{name}'。");
        }

        return value!;
    }

    private static void SetIfPresent(
        SqlConnectionStringBuilder builder,
        IReadOnlyDictionary<string, string> fields,
        Action<string> setter,
        params string[] fieldNames)
    {
        foreach (var fieldName in fieldNames)
        {
            if (fields.TryGetValue(fieldName, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                setter(value!);
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
