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

    /// <summary>
    /// 命名参数（SEC-0）：以 <c>@name</c> 占位符形式将上游数据以绑定参数传入，杜绝将上游值拼接入 SQL 文本导致的注入。
    /// 每个值是一个脚本（如 <c>$json.name</c>），按当前输入项逐项求值，运行时经 Dapper 命名参数绑定执行。
    /// </summary>
    [Description("Named bound parameters (e.g. @name). Each value is a script evaluated per input item; values are passed as bound parameters, never concatenated into SQL text.")]
    public Dictionary<string, Script>? Parameters { get; set; }

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
            foreach (var (item, index, sql) in sqlStatements)
            {
                // SEC-0：将命名参数（如 @name）按当前输入项逐项求值后，以绑定参数形式传入，杜绝 SQL 文本拼接注入。
                var parameters = await ResolveParametersAsync(context, item, index, cancellationToken).ConfigureAwait(false);

                await using var reader = await executor
                    .ExecuteReaderAsync(sql, parameters, cancellationToken, Timeout)
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

        // SEC-0：拒绝将上游数据（$json/$input/$item/$items 等）直接拼入 SQL 文本。
        // 上游值必须以 @name 命名参数（Parameters）的形式绑定执行，杜绝字符串拼接注入。
        if (ReferencesUpstreamData(Sql.Source))
        {
            return (null, context.ErrorResult("UnsafeSqlInterpolation",
                "dbRead 不允许将上游数据（$json/$input/$item/$items）直接拼入 SQL 文本；请改用 @name 命名参数（Parameters）。"));
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
    /// 判断 SQL 表达式源码是否直接引用了上游数据变量（$json/$input/$item/$items 等）。
    /// 直接拼入 SQL 文本属于注入风险，必须以 @name 命名参数（Parameters）绑定。
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex UpstreamDataReferencePattern =
        new(@"(?<!\w)\$(json|input|items|itemIndex|item|runIndex)\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool ReferencesUpstreamData(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        return UpstreamDataReferencePattern.IsMatch(source);
    }

    /// <summary>
    /// 逐项求值 <see cref="Parameters"/> 中的命名参数脚本，得到绑定参数字典（SEC-0）。
    /// 未配置参数时返回 <c>null</c>（由 <see cref="DbExecutor"/> 等效于无参数执行）。
    /// 每个值经脚本引擎求值（如 <c>$json.name</c> → 上游字段），转换后的 CLR 值交由 Dapper 以命名参数绑定，
    /// 绝不以字符串拼接方式嵌入 SQL 文本。
    /// </summary>
    private async Task<IReadOnlyDictionary<string, object?>?> ResolveParametersAsync(
        NodeExecutionContext context,
        JsonNode? item,
        int itemIndex,
        CancellationToken cancellationToken)
    {
        if (Parameters is null || Parameters.Count == 0)
        {
            return null;
        }

        var resolved = new Dictionary<string, object?>(Parameters.Count, StringComparer.Ordinal);
        foreach (var (name, script) in Parameters)
        {
            if (script is null) continue;

            resolved[name] = await script
                .EvaluateAsync<object?>(context, item, itemIndex, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        return resolved;
    }

    /// <summary>
    /// 判断语句是否为只读（首个有效关键字为 <c>SELECT</c> 或 <c>WITH</c>，跳过空白与注释）。
    /// 任何其它关键字（INSERT/UPDATE/DELETE/DROP/TRUNCATE/MERGE/ALTER 等）或非标识符开头一律拒绝。
    /// </summary>
    private static bool IsReadOnlyStatement(string sql)
    {
        var keyword = SqlStatementScanner.ExtractFirstKeyword(sql);
        if (keyword is null) return false;

        if (!string.Equals(keyword, "SELECT", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(keyword, "WITH", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Reject SELECT ... INTO (writes to a target table) and stacked statements
        // separated by ';' (a second statement after the first).
        if (SqlStatementScanner.ContainsKeyword(sql, "INTO")) return false;
        if (SqlStatementScanner.HasTrailingStatement(sql)) return false;

        return true;
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
