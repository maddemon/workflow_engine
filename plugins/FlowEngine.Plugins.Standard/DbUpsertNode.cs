using Acornima.Ast;
using System.ComponentModel;
using System.Data.Common;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Plugins.Standard.Data;
using Jint;

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
    /// 数据库连接字符串或表达式（如 <c>$credentials.db.connectionString</c>）。
    /// </summary>
    [Hint(PresentationHint.Expression)]
    [Description("Connection string or expression (e.g. $credentials.db.connectionString).")]
    public string Connection { get; set; } = string.Empty;

    /// <summary>
    /// 目标表名。
    /// </summary>
    [Description("Target table name.")]
    public string Table { get; set; } = string.Empty;

    /// <summary>
    /// 写入模式：upsert、insert、update。
    /// </summary>
    [Description("Write mode: upsert | insert | update.")]
    public string Mode { get; set; } = "upsert";

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
    public Dictionary<string, string> Columns { get; set; } = [];

    /// <summary>
    /// 可选方言；留空时从连接字符串推断。
    /// </summary>
    [Description("Optional dialect. Inferred from connection string when omitted.")]
    public string? Dialect { get; set; }

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
            var connectionString = ResolveConnection(context);
            if (connectionString is null)
            {
                return context.ErrorResult("MissingConnection", "Connection string is required.");
            }

            var validationError = Validate(context, connectionString, out var keyColumnList, out var columnList);
            if (validationError is not null)
            {
                return validationError;
            }

            var mode = Mode.Trim();
            if (!mode.Equals("upsert", StringComparison.OrdinalIgnoreCase) &&
                !mode.Equals("insert", StringComparison.OrdinalIgnoreCase) &&
                !mode.Equals("update", StringComparison.OrdinalIgnoreCase))
            {
                return context.ErrorResult("InvalidMode", $"Mode must be 'upsert', 'insert' or 'update', got '{Mode}'.");
            }

            if ((mode.Equals("upsert", StringComparison.OrdinalIgnoreCase) || mode.Equals("update", StringComparison.OrdinalIgnoreCase))
                && keyColumnList!.Count == 0)
            {
                return context.ErrorResult("MissingKeyColumns", "KeyColumns is required for upsert/update mode.");
            }

            var dialect = DbDialectResolver.Resolve(Dialect, connectionString);
            var generator = SqlGeneratorFactory.Create(dialect);
            var sql = mode.ToLowerInvariant() switch
            {
                "upsert" => generator.BuildUpsertSql(Table, columnList!, keyColumnList!),
                "insert" => generator.BuildInsertSql(Table, columnList!),
                "update" => generator.BuildUpdateSql(Table, columnList!, keyColumnList!),
                _ => throw new InvalidOperationException($"Unexpected mode '{Mode}'.")
            };

            var inputBatch = context.Inputs.TryGetValue(FlowConstants.PortNames.Input, out var batch)
                ? batch
                : new DataBatch();

            if (inputBatch.Items.Count == 0)
            {
                return CreateResult(context, true, 0, 0, 0);
            }

            var allItems = inputBatch.Items.Select(i => (object?)i.Data).ToList();
            var preparedExpressions = Columns.Values.Select(JsEngine.PrepareExpression).ToList();

            // 单引擎复用：全局变量只注入一次，逐项变量在循环内覆盖。
            using var engine = JsEngine.Create();
            engine.ApplyGlobalVariables(context);

            await using var connection = DbConnectionFactory.CreateConnection(dialect, connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var affectedRows = 0;
            var inserted = 0;
            var updated = 0;
            var isUpsert = mode.Equals("upsert", StringComparison.OrdinalIgnoreCase);

            try
            {
                for (var itemIndex = 0; itemIndex < inputBatch.Items.Count; itemIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var item = inputBatch.Items[itemIndex];
                    var values = EvaluateRowValues(engine, preparedExpressions, allItems, item.Data, itemIndex, context);

                    if (mode.Equals("update", StringComparison.OrdinalIgnoreCase))
                    {
                        values = ReorderUpdateValues(values, columnList!, keyColumnList!);
                    }

                    var rowExisted = false;
                    if (isUpsert)
                    {
                        var keyValues = GetKeyValues(values, columnList!, keyColumnList!);
                        rowExisted = await RowExistsAsync(
                            connection,
                            transaction,
                            Table,
                            keyColumnList!,
                            keyValues,
                            generator,
                            cancellationToken).ConfigureAwait(false);
                    }

                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = sql;
                    for (var i = 0; i < values.Count; i++)
                    {
                        var parameter = command.CreateParameter();
                        parameter.ParameterName = $"@p{i}";
                        parameter.Value = values[i] ?? DBNull.Value;
                        command.Parameters.Add(parameter);
                    }

                    affectedRows += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

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

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort rollback; the original exception will be handled below.
                }

                throw;
            }

            if (mode.Equals("insert", StringComparison.OrdinalIgnoreCase))
            {
                inserted = affectedRows;
            }
            else if (mode.Equals("update", StringComparison.OrdinalIgnoreCase))
            {
                updated = affectedRows;
            }

            return CreateResult(context, true, affectedRows, inserted, updated);
        }
        catch (OperationCanceledException)
        {
            return context.ErrorResult("Cancelled", "Database operation was cancelled.");
        }
        catch (DbException ex)
        {
            return context.ErrorResult("DbError", $"Database error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return context.ErrorResult("UnexpectedError", $"Unexpected database error: {ex.Message}");
        }
    }

    private string? ResolveConnection(NodeExecutionContext context)
    {
        if (context.ResolvedParameters.TryGetValue("connection", out var resolved) && resolved is string resolvedString && !string.IsNullOrWhiteSpace(resolvedString))
        {
            return resolvedString;
        }

        if (string.IsNullOrWhiteSpace(Connection))
        {
            return null;
        }

        var trimmed = Connection.TrimStart();
        if (trimmed.StartsWith('$') || trimmed.StartsWith('\'') || trimmed.StartsWith('"'))
        {
            try
            {
                using var engine = JsEngine.Create();
                var result = engine.Evaluate(Connection);
                var value = JsEngine.ToClrValue(result) as string;
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            catch
            {
                return null;
            }
        }

        return Connection;
    }

    private NodeExecutionResult? Validate(NodeExecutionContext context, string connectionString, out List<string>? keyColumnList, out List<string>? columnList)
    {
        keyColumnList = null;
        columnList = null;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return context.ErrorResult("MissingConnection", "Connection string is required.");
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

    private List<object?> EvaluateRowValues(
        JsEngine engine,
        IReadOnlyList<Prepared<Script>> preparedExpressions,
        List<object?> allItems,
        JsonNode? currentItem,
        int itemIndex,
        NodeExecutionContext context)
    {
        engine.ApplyItemScope(context, currentItem, allItems, itemIndex);

        var values = new List<object?>();
        foreach (var prepared in preparedExpressions)
        {
            var result = engine.EvaluatePrepared(prepared);
            values.Add(JsEngine.ToClrValue(result));
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
        DbConnection connection,
        DbTransaction transaction,
        string table,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<object?> keyValues,
        IDbSqlGenerator generator,
        CancellationToken cancellationToken)
    {
        var quotedTable = generator.QuoteIdentifier(table);
        var where = string.Join(" AND ", keyColumns.Select((c, i) => $"{generator.QuoteIdentifier(c)} = @p{i}"));
        var sql = $"SELECT 1 FROM {quotedTable} WHERE {where}";

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        for (var i = 0; i < keyValues.Count; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"@p{i}";
            parameter.Value = keyValues[i] ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
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
