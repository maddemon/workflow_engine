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
/// 数据库只读查询节点。仅允许执行 <c>SELECT</c> / <c>WITH</c> 语句，把查询结果逐行映射为 <see cref="DataItem"/>。
/// </summary>
public sealed class DbReadNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "dbRead";

    /// <inheritdoc />
    public string DisplayName => "DB Read";

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
    /// SQL 语句（JS 表达式）。表达式求值结果为字符串，支持 <c>$input</c> / <c>$json</c> 注入参数。
    /// 仅允许以 <c>SELECT</c> 或 <c>WITH</c> 开头的只读语句。
    /// </summary>
    [Hint(PresentationHint.Script)]
    [Description("SQL statement as a JS expression (supports $input/$json). Only SELECT/WITH read-only statements are permitted.")]
    public Script? Sql { get; set; }

    /// <summary>
    /// 可选命令超时（秒）。留空时使用对应驱动/连接字符串的默认超时。
    /// </summary>
    [Description("Optional command timeout in seconds. When omitted, the driver/connection default is used.")]
    public int? Timeout { get; set; }

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

            // 方言与连接字符串：复用 DbUpsertNode 同款组件，不重复连接构建逻辑。
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

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return context.ErrorResult(FlowConstants.ErrorCodes.MissingConnection, "Connection string is required.");
            }

            // 无输入时以空批执行（触发器直连场景），仅求值一次；有输入时逐项求值以支持 $json/$input 注入。
            var inputBatch = context.GetInputBatch();
            var items = inputBatch.Items;

            var sqlStatements = new List<(JsonNode? Item, int Index, string Sql)>();
            if (items.Count == 0)
            {
                var (sql, error) = await ResolveSqlAsync(context, null, 0, cancellationToken).ConfigureAwait(false);
                if (error is not null) return error;
                sqlStatements.Add((null, 0, sql!));
            }
            else
            {
                for (var index = 0; index < items.Count; index++)
                {
                    var (sql, error) = await ResolveSqlAsync(context, items[index].Data, index, cancellationToken).ConfigureAwait(false);
                    if (error is not null) return error;
                    sqlStatements.Add((items[index].Data, index, sql!));
                }
            }

            await using var executor = await DbExecutor.CreateAsync(dialect, connectionString, cancellationToken, context.EngineLogger).ConfigureAwait(false);

            var output = new DataBatch();
            foreach (var (_, _, sql) in sqlStatements)
            {
                await using var reader = await executor
                    .ExecuteReaderAsync(sql, null, cancellationToken, Timeout)
                    .ConfigureAwait(false);

                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    output.Items.Add(new DataItem
                    {
                        Data = MapRow(reader),
                        Success = true,
                        SourceIndex = output.Items.Count
                    });
                }

                await reader.DisposeAsync().ConfigureAwait(false);
            }

            await executor.CommitAsync(cancellationToken).ConfigureAwait(false);
            return context.Ok(output);
        }
        catch (OperationCanceledException)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.Cancelled, "Database read was cancelled.");
        }
        catch (DbException ex)
        {
            // 仅记录非敏感信息，绝不输出连接字符串或凭据。
            context.Logger?.LogError(ex, "dbRead 执行数据库查询失败（方言已配置，详情见消息）。");
            return context.ErrorResult("DbError", $"Database error: {ex.Message}");
        }
        catch (ScriptErrorException ex)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.ScriptError, $"Sql expression evaluation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.UnexpectedError, $"Unexpected database error: {ex.Message}");
        }
    }

    /// <summary>
    /// 求值 Sql 表达式并做只读校验。
    /// </summary>
    private async Task<(string? Sql, NodeExecutionResult? Error)> ResolveSqlAsync(
        NodeExecutionContext context,
        JsonNode? item,
        int itemIndex,
        CancellationToken cancellationToken)
    {
        if (Sql is null)
        {
            return (null, context.ErrorResult("InvalidSql", "Sql expression is required."));
        }

        var sql = await Sql.EvaluateAsync<string>(context, item, itemIndex, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(sql))
        {
            return (null, context.ErrorResult("InvalidSql", "Sql expression must evaluate to a non-empty statement."));
        }

        if (!IsReadOnlyStatement(sql!))
        {
            return (null, context.ErrorResult("ReadOnlyViolation", "dbRead only permits SELECT/WITH statements."));
        }

        return (sql, null);
    }

    /// <summary>
    /// 判断语句是否为只读（首个有效关键字为 <c>SELECT</c> 或 <c>WITH</c>，跳过空白与注释）。
    /// 任何其它关键字（INSERT/UPDATE/DELETE/DROP/TRUNCATE/MERGE/ALTER 等）或非标识符开头一律拒绝。
    /// </summary>
    private static bool IsReadOnlyStatement(string sql)
    {
        var keyword = ExtractFirstKeyword(sql);
        if (keyword is null) return false;

        return string.Equals(keyword, "SELECT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(keyword, "WITH", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractFirstKeyword(string sql)
    {
        var i = 0;
        var length = sql.Length;
        while (i < length)
        {
            var c = sql[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // 行注释 --
            if (c == '-' && i + 1 < length && sql[i + 1] == '-')
            {
                while (i < length && sql[i] != '\n') i++;
                continue;
            }

            // 块注释 /* ... */
            if (c == '/' && i + 1 < length && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < length && (sql[i] != '*' || sql[i + 1] != '/')) i++;
                i += 2;
                continue;
            }

            // 读取标识符（字母/数字/下划线）。非标识符开头（如 '('、';'、字符串字面量等）直接拒绝。
            var start = i;
            while (i < length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_')) i++;

            if (i == start) return null;

            return sql.Substring(start, i - start);
        }

        return null;
    }

    /// <summary>
    /// 将一行 <see cref="DbDataReader"/> 映射为 <see cref="JsonObject"/>，列名即键。
    /// 处理常见数据库类型并安全处理 NULL，绝不因未知类型或 NULL 崩溃。
    /// </summary>
    private static JsonObject MapRow(DbDataReader reader)
    {
        var obj = new JsonObject();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var columnName = reader.GetName(i);
            if (string.IsNullOrEmpty(columnName)) continue;

            obj[columnName] = ConvertValue(reader.GetValue(i));
        }

        return obj;
    }

    private static JsonNode? ConvertValue(object value)
    {
        if (value is DBNull or null) return null;

        return value switch
        {
            string s => JsonValue.Create(s),
            bool b => JsonValue.Create(b),
            int or long or short or byte or sbyte or uint or ushort or ulong => JsonValue.Create(Convert.ToInt64(value)),
            decimal or double or float => JsonValue.Create(Convert.ToDecimal(value)),
            DateTime dt => JsonValue.Create(dt),
            DateTimeOffset dto => JsonValue.Create(dto.UtcDateTime),
            Guid g => JsonValue.Create(g.ToString()),
            byte[] bytes => JsonValue.Create(Convert.ToBase64String(bytes)),
            _ => JsonValue.Create(value.ToString())
        };
    }
}
