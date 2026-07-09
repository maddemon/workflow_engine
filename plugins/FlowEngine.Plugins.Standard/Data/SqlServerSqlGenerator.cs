namespace FlowEngine.Plugins.Standard.Data;

/// <summary>
/// SQL Server 方言 SQL 生成器。
/// </summary>
public sealed class SqlServerSqlGenerator : IDbSqlGenerator
{
    /// <inheritdoc />
    public string QuoteIdentifier(string identifier)
    {
        IdentifierValidator.EnsureValid(identifier, "标识符");
        return $"[{identifier}]";
    }

    /// <inheritdoc />
    public string BuildInsertSql(string table, IReadOnlyList<string> columns)
    {
        IdentifierValidator.EnsureValid(table, "表名");
        var quotedTable = QuoteIdentifier(table);
        var quotedColumns = string.Join(", ", columns.Select(QuoteIdentifier));
        var values = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
        return $"INSERT INTO {quotedTable} ({quotedColumns}) VALUES ({values})";
    }

    /// <inheritdoc />
    public string BuildUpsertSql(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns)
    {
        IdentifierValidator.EnsureValid(table, "表名");
        if (keyColumns.Count == 0)
        {
            throw new ArgumentException("upsert 必须指定主键列。", nameof(keyColumns));
        }

        var quotedTable = QuoteIdentifier(table);
        var quotedColumns = string.Join(", ", columns.Select(QuoteIdentifier));
        var values = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
        var onConditions = string.Join(" AND ", keyColumns.Select(c => $"target.{QuoteIdentifier(c)} = src.{QuoteIdentifier(c)}"));
        var updateColumns = GetUpdateColumns(columns, keyColumns);
        var updates = string.Join(", ", updateColumns.Select(c => $"target.{QuoteIdentifier(c)} = src.{QuoteIdentifier(c)}"));
        var insertColumns = string.Join(", ", columns.Select(QuoteIdentifier));
        var insertValues = string.Join(", ", columns.Select(c => $"src.{QuoteIdentifier(c)}"));

        return $"MERGE INTO {quotedTable} AS target USING (VALUES ({values})) AS src ({insertColumns}) ON {onConditions} WHEN MATCHED THEN UPDATE SET {updates} WHEN NOT MATCHED THEN INSERT ({insertColumns}) VALUES ({insertValues});";
    }

    /// <inheritdoc />
    public string BuildUpdateSql(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns)
    {
        IdentifierValidator.EnsureValid(table, "表名");
        if (keyColumns.Count == 0)
        {
            throw new ArgumentException("update 必须指定主键列。", nameof(keyColumns));
        }

        var setColumns = columns.Except(keyColumns).ToList();
        if (setColumns.Count == 0)
        {
            throw new ArgumentException("update 至少需要存在一个非主键列。", nameof(columns));
        }

        var quotedTable = QuoteIdentifier(table);
        var sets = string.Join(", ", setColumns.Select((c, i) => $"{QuoteIdentifier(c)} = @p{i}"));
        var keyStart = setColumns.Count;
        var where = string.Join(" AND ", keyColumns.Select((c, i) => $"{QuoteIdentifier(c)} = @p{keyStart + i}"));

        return $"UPDATE {quotedTable} SET {sets} WHERE {where}";
    }

    private static List<string> GetUpdateColumns(IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns)
    {
        var nonKey = columns.Except(keyColumns).ToList();
        return nonKey.Count > 0 ? nonKey : columns.ToList();
    }
}
