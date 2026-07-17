using System.ComponentModel;
using System.Data.Common;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;
using FlowEngine.Plugins.Standard.Data;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 通用数据库写入节点，支持 upsert / insert / update。
/// </summary>
public sealed class DbUpsertNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "dbUpsert";

    /// <inheritdoc />
    public string DisplayName => "DB Upsert";

    /// <inheritdoc />
    public string Category => "Data";

    /// <inheritdoc />
    public string Icon => "database";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 数据库连接凭据（类型为 <c>connectionString</c>）。凭据按结构化字段（dbType/host/port/database/userid/password 等）
    /// 配置，运行时由对应方言的 <see cref="IConnectionStringBuilder"/> 生成 ADO.NET 连接字符串。
    /// </summary>
    [Credential("connectionString")]
    [Description("Database connection credential (type: database). Connection string is generated per dialect from its fields (dbType/host/port/database/userid/password).")]
    public CredentialValue? Connection { get; set; }

    /// <summary>
    /// 目标表名。
    /// </summary>
    [Description("Target table name.")]
    public string Table { get; set; } = string.Empty;

/// <summary>
/// 写入模式：upsert、insert、update。
/// </summary>
[Description("Write mode: upsert | insert | update.")]
public DbUpsertMode Mode { get; set; } = DbUpsertMode.Upsert;

    /// <summary>
    /// 主键列，逗号分隔（upsert/update 必填）。
    /// </summary>
    [Description("Primary key columns, comma-separated (required for upsert/update).")]
    public string KeyColumns { get; set; } = string.Empty;

    /// <summary>
    /// 列映射：键为数据库列名，值为 JS 表达式（如 <c>$input.item().userid</c> 或 <c>$json.userid</c>）。
    /// </summary>
    [Hint(PresentationHint.Script)]
    [Description("Column mapping: key = DB column, value = JS expression evaluated per row.")]
    public Dictionary<string, Script> Columns { get; set; } = [];

/// <summary>
/// 可选方言覆盖；留空时从凭据的 <c>dbType</c> 字段推断。
/// </summary>
[Description("Optional dialect override. When omitted, inferred from the credential's dbType field.")]
public DbDialect? Dialect { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (Connection is null)
            {
                return context.ErrorResult(FlowConstants.ErrorCodes.MissingConnection, "Connection credential is required.");
            }

            // 方言：节点 Dialect 参数优先，否则取凭据的 dbType 字段
            DbDialect dialect;
            string connectionString;
            try
            {
                dialect = Dialect
                    ?? DbDialectResolver.ParseDbType(Connection.Fields.TryGetValue(FlowConstants.CredentialFields.DbType, out var dbType) ? dbType : null);
                connectionString = ConnectionStringBuilderFactory.Get(dialect).Build(Connection.Fields);
            }
            catch (InvalidOperationException ex)
            {
                return context.ErrorResult(FlowConstants.ErrorCodes.MissingConnection, ex.Message);
            }

            var validationError = Validate(context, connectionString, out var keyColumnList, out var columnList);
            if (validationError is not null)
            {
                return validationError;
            }

            if ((Mode == DbUpsertMode.Upsert || Mode == DbUpsertMode.Update)
                && keyColumnList!.Count == 0)
            {
                return context.ErrorResult("MissingKeyColumns", "KeyColumns is required for upsert/update mode.");
            }

            var generator = SqlGeneratorFactory.Create(dialect);
            var sql = Mode switch
            {
                DbUpsertMode.Upsert => generator.BuildUpsertSql(Table, columnList!, keyColumnList!),
                DbUpsertMode.Insert => generator.BuildInsertSql(Table, columnList!),
                DbUpsertMode.Update => generator.BuildUpdateSql(Table, columnList!, keyColumnList!),
                _ => throw new InvalidOperationException($"Unexpected mode '{Mode}'.")
            };

            var inputBatch = context.GetInputBatch();

            if (inputBatch.Items.Count == 0)
            {
                return CreateResult(context, true, 0, 0, 0);
            }

            await using var executor = await DbExecutor.CreateAsync(dialect, connectionString, cancellationToken).ConfigureAwait(false);

            var affectedRows = 0;
            var inserted = 0;
            var updated = 0;
            var isUpsert = Mode == DbUpsertMode.Upsert;

            for (var itemIndex = 0; itemIndex < inputBatch.Items.Count; itemIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var item = inputBatch.Items[itemIndex];
                var values = await EvaluateRowValues(Columns, item.Data, itemIndex, context, cancellationToken).ConfigureAwait(false);

                if (Mode == DbUpsertMode.Update)
                {
                    values = ReorderUpdateValues(values, columnList!, keyColumnList!);
                }

                var rowExisted = false;
                if (isUpsert)
                {
                    var keyValues = GetKeyValues(values, columnList!, keyColumnList!);
                    rowExisted = await RowExistsAsync(
                        executor,
                        Table,
                        keyColumnList!,
                        keyValues,
                        generator,
                        cancellationToken).ConfigureAwait(false);
                }

                affectedRows += await executor.ExecuteNonQueryAsync(sql, values, cancellationToken).ConfigureAwait(false);

                if (isUpsert)
                {
                    if (rowExisted)
                    {
                        updated++;
                    }
                    else
                    {
                        inserted++;
                    }
                }
            }

            await executor.CommitAsync(cancellationToken).ConfigureAwait(false);

            if (Mode == DbUpsertMode.Insert)
            {
                inserted = affectedRows;
            }
            else if (Mode == DbUpsertMode.Update)
            {
                updated = affectedRows;
            }

            return CreateResult(context, true, affectedRows, inserted, updated);
        }
        catch (OperationCanceledException)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.Cancelled, "Database operation was cancelled.");
        }
        catch (DbException ex)
        {
            return context.ErrorResult("DbError", $"Database error: {ex.Message}");
        }
        catch (ScriptErrorException ex)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.ScriptError, $"Column expression evaluation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.UnexpectedError, $"Unexpected database error: {ex.Message}");
        }
    }

    private NodeExecutionResult? Validate(NodeExecutionContext context, string connectionString, out List<string>? keyColumnList, out List<string>? columnList)
    {
        keyColumnList = null;
        columnList = null;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.MissingConnection, "Connection string is required.");
        }

        if (string.IsNullOrWhiteSpace(Table))
        {
            return context.ErrorResult("MissingTable", "Table is required.");
        }

        if (!IdentifierValidator.IsValid(Table))
        {
            return context.ErrorResult("InvalidTable", $"Table name '{Table}' contains invalid characters.");
        }

        var columns = Columns ?? [];
        if (columns.Count == 0)
        {
            return context.ErrorResult("MissingColumns", "Columns mapping is required.");
        }

        foreach (var columnName in columns.Keys)
        {
            if (!IdentifierValidator.IsValid(columnName))
            {
                return context.ErrorResult("InvalidColumn", $"Column name '{columnName}' contains invalid characters.");
            }
        }

        columnList = columns.Keys.ToList();

        keyColumnList = KeyColumns
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .ToList();

        foreach (var key in keyColumnList)
        {
            if (!IdentifierValidator.IsValid(key))
            {
                return context.ErrorResult("InvalidKeyColumn", $"Key column '{key}' contains invalid characters.");
            }
        }

        foreach (var key in keyColumnList)
        {
            if (!columns.ContainsKey(key))
            {
                return context.ErrorResult("MissingKeyColumn", $"Key column '{key}' is not defined in Columns mapping.");
            }
        }

        return null;
    }

    private async Task<List<object?>> EvaluateRowValues(
        IReadOnlyDictionary<string, Script> columns,
        JsonNode? currentItem,
        int itemIndex,
        NodeExecutionContext context,
        CancellationToken cancellationToken)
    {
        var values = new List<object?>();
        foreach (var (_, columnScript) in columns)
        {
            var value = await columnScript.EvaluateAsync<object>(context, currentItem, itemIndex, cancellationToken: cancellationToken).ConfigureAwait(false);
            values.Add(value);
        }

        return values;
    }

    private static List<object?> ReorderUpdateValues(
        List<object?> values,
        List<string> columns,
        List<string> keyColumns)
    {
        var setColumns = columns.Except(keyColumns).ToList();
        var reordered = new List<object?>(values.Count);
        foreach (var column in setColumns)
        {
            reordered.Add(values[columns.IndexOf(column)]);
        }

        foreach (var column in keyColumns)
        {
            reordered.Add(values[columns.IndexOf(column)]);
        }

        return reordered;
    }

    private static List<object?> GetKeyValues(
        List<object?> values,
        List<string> columns,
        List<string> keyColumns)
    {
        var keyValues = new List<object?>(keyColumns.Count);
        foreach (var key in keyColumns)
        {
            var index = columns.IndexOf(key);
            keyValues.Add(values[index]);
        }

        return keyValues;
    }

    private static async Task<bool> RowExistsAsync(
        DbExecutor executor,
        string table,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<object?> keyValues,
        IDbSqlGenerator generator,
        CancellationToken cancellationToken)
    {
        var quotedTable = generator.QuoteIdentifier(table);
        var where = string.Join(" AND ", keyColumns.Select((c, i) => $"{generator.QuoteIdentifier(c)} = @p{i}"));
        var sql = $"SELECT 1 FROM {quotedTable} WHERE {where}";
        var result = await executor.ExecuteScalarAsync(sql, keyValues, cancellationToken).ConfigureAwait(false);
        return result is not null && result != DBNull.Value;
    }

    private static NodeExecutionResult CreateResult(NodeExecutionContext context, bool success, int affectedRows, int inserted, int updated)
    {
        return context.CreateSingleResult(new JsonObject
        {
            ["success"] = success,
            ["affectedRows"] = affectedRows,
            ["inserted"] = inserted,
            ["updated"] = updated
        }, success);
    }
}
