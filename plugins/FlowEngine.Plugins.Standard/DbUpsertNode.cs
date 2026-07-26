using System;
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
[NodeMeta(TypeName = "dbUpsert", DisplayName = "DB Upsert", Category = NodeCategory.Data, Icon = "database", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class DbUpsertNode : NodeBase
{
    [Inject] public NodeExecutionContext Ctx { get; private set; } = null!;
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
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        try
        {
            if (Connection is null)
            {
                throw new NodeExecutionException(FlowConstants.ErrorCodes.MissingConnection, "Connection credential is required.");
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
                throw new NodeExecutionException(FlowConstants.ErrorCodes.MissingConnection, ex.Message);
            }

            Validate(connectionString, out var keyColumnList, out var columnList);

            if ((Mode == DbUpsertMode.Upsert || Mode == DbUpsertMode.Update)
                && keyColumnList!.Count == 0)
            {
                throw new NodeExecutionException("MissingKeyColumns", "KeyColumns is required for upsert/update mode.");
            }

            var generator = SqlGeneratorFactory.Create(dialect);
            var sql = Mode switch
            {
                DbUpsertMode.Upsert => generator.BuildUpsertSql(Table, columnList!, keyColumnList!),
                DbUpsertMode.Insert => generator.BuildInsertSql(Table, columnList!),
                DbUpsertMode.Update => generator.BuildUpdateSql(Table, columnList!, keyColumnList!),
                _ => throw new InvalidOperationException($"Unexpected mode '{Mode}'.")
            };

            var inputBatch = input.InputBatch;

            if (inputBatch.Items.Count == 0)
            {
                return CreateResult(true, 0, 0, 0);
            }

            await using var executor = await DbExecutor.CreateAsync(dialect, connectionString, ct, Ctx.EngineLogger).ConfigureAwait(false);

            var affectedRows = 0;
            var inserted = 0;
            var updated = 0;
            var isUpsert = Mode == DbUpsertMode.Upsert;

            for (var itemIndex = 0; itemIndex < inputBatch.Items.Count; itemIndex++)
            {
                ct.ThrowIfCancellationRequested();

                var item = inputBatch.Items[itemIndex];
                var values = await EvaluateRowValues(Columns, item.Data, itemIndex, ct).ConfigureAwait(false);

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
                        ct).ConfigureAwait(false);
                }

                affectedRows += await executor.ExecuteNonQueryAsync(sql, values, ct).ConfigureAwait(false);

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

            await executor.CommitAsync(ct).ConfigureAwait(false);

            if (Mode == DbUpsertMode.Insert)
            {
                inserted = affectedRows;
            }
            else if (Mode == DbUpsertMode.Update)
            {
                updated = affectedRows;
            }

            return CreateResult(true, affectedRows, inserted, updated);
        }
        catch (OperationCanceledException)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.Cancelled, "Database operation was cancelled.");
        }
        catch (DbException ex)
        {
            throw new NodeExecutionException("DbError", $"Database error: {ex.Message}");
        }
        catch (ScriptErrorException ex)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.ScriptError, $"Column expression evaluation failed: {ex.Message}");
        }
        catch (Exception ex) when (ex is not NodeExecutionException)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.UnexpectedError, $"Unexpected database error: {ex.Message}");
        }
    }

    private void Validate(string connectionString, out List<string>? keyColumnList, out List<string>? columnList)
    {
        keyColumnList = null;
        columnList = null;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.MissingConnection, "Connection string is required.");
        }

        if (string.IsNullOrWhiteSpace(Table))
        {
            throw new NodeExecutionException("MissingTable", "Table is required.");
        }

        if (!IdentifierValidator.IsValid(Table))
        {
            throw new NodeExecutionException("InvalidTable", $"Table name '{Table}' contains invalid characters.");
        }

        var columns = Columns ?? [];
        if (columns.Count == 0)
        {
            throw new NodeExecutionException("MissingColumns", "Columns mapping is required.");
        }

        foreach (var columnName in columns.Keys)
        {
            if (!IdentifierValidator.IsValid(columnName))
            {
                throw new NodeExecutionException("InvalidColumn", $"Column name '{columnName}' contains invalid characters.");
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
                throw new NodeExecutionException("InvalidKeyColumn", $"Key column '{key}' contains invalid characters.");
            }
        }

        foreach (var key in keyColumnList)
        {
            if (!columns.ContainsKey(key))
            {
                throw new NodeExecutionException("MissingKeyColumn", $"Key column '{key}' is not defined in Columns mapping.");
            }
        }
    }

    private async Task<List<object?>> EvaluateRowValues(
        IReadOnlyDictionary<string, Script> columns,
        JsonNode? currentItem,
        int itemIndex,
        CancellationToken ct)
    {
        var values = new List<object?>();
        foreach (var (_, columnScript) in columns)
        {
            var value = await columnScript.EvaluateAsync<object>(Ctx, item: currentItem, itemIndex: itemIndex, cancellationToken: ct).ConfigureAwait(false);
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
        CancellationToken ct)
    {
        var quotedTable = generator.QuoteIdentifier(table);
        var where = string.Join(" AND ", keyColumns.Select((c, i) => $"{generator.QuoteIdentifier(c)} = @p{i}"));
        var sql = $"SELECT 1 FROM {quotedTable} WHERE {where}";
        var result = await executor.ExecuteScalarAsync(sql, keyValues, ct).ConfigureAwait(false);
        return result is not null && result != DBNull.Value;
    }

    /// <summary>
    /// 将单条 JSON 对象包装为单条 DataItem 的成功输出。
    /// </summary>
    private static NodeHandlerOutput CreateResult(bool success, int affectedRows, int inserted, int updated)
    {
        return Single(new JsonObject
        {
            ["success"] = success,
            ["affectedRows"] = affectedRows,
            ["inserted"] = inserted,
            ["updated"] = updated
        });
    }

    /// <summary>
    /// 将单条 JSON 对象包装为单条 DataItem 的输出。
    /// </summary>
    private static NodeHandlerOutput Single(JsonObject obj) =>
        NodeHandlerOutput.Data(new DataBatch
        {
            Items = [ new DataItem { Data = obj, Success = true, SourceIndex = 0 } ]
        });
}