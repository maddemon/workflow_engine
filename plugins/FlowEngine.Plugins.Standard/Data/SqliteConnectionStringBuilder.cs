using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace FlowEngine.Plugins.Standard.Data;

/// <summary>
/// SQLite 连接字符串生成器。直接使用 Microsoft.Data.Sqlite 自带的 <see cref="Microsoft.Data.Sqlite.SqliteConnectionStringBuilder"/> 组装。
/// 注意：本类名与提供程序类名相同，故使用 <see cref="Microsoft.Data.Sqlite.SqliteConnectionStringBuilder"/> 全名以避免歧义。
/// 读取字段：dataSource（必填，文件路径或 :memory:）、mode、cache（可选，如 Memory / Shared）。
/// </summary>
public sealed class SqliteConnectionStringBuilder : IConnectionStringBuilder
{
    /// <inheritdoc />
    public DbDialect Dialect => DbDialect.SQLite;

    /// <inheritdoc />
    public string Build(IReadOnlyDictionary<string, string> fields)
    {
        var dataSource = GetField(fields, "dataSource");
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            throw new InvalidOperationException("凭据缺少必填字段 'dataSource'。");
        }

        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = dataSource!
        };

        if (fields.TryGetValue("mode", out var mode) && !string.IsNullOrWhiteSpace(mode) &&
            Enum.TryParse<SqliteOpenMode>(mode, true, out var openMode))
        {
            builder.Mode = openMode;
        }

        if (fields.TryGetValue("cache", out var cache) && !string.IsNullOrWhiteSpace(cache) &&
            Enum.TryParse<SqliteCacheMode>(cache, true, out var cacheMode))
        {
            builder.Cache = cacheMode;
        }

        return builder.ConnectionString;
    }

    private static string? GetField(IReadOnlyDictionary<string, string> fields, string name)
    {
        return fields.TryGetValue(name, out var value) ? value : null;
    }
}
