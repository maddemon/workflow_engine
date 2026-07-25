using System.Data.Common;
using Dapper;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Plugins.Standard.Data;

/// <summary>
/// 数据库执行器，封装连接/事务生命周期。
/// 调用方负责提供 SQL、参数和逐行值计算。
/// </summary>
public sealed class DbExecutor : IAsyncDisposable
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly ILogger? _logger;
    private bool _committed;
    private bool _disposed;

    private DbExecutor(DbConnection connection, DbTransaction transaction, ILogger? logger)
    {
        _connection = connection;
        _transaction = transaction;
        _logger = logger;
    }

    /// <summary>
    /// 创建并打开数据库连接和事务。
    /// </summary>
    public static async Task<DbExecutor> CreateAsync(DbDialect dialect, string connectionString, CancellationToken ct, ILogger? logger = null)
    {
        var connection = DbConnectionFactory.CreateConnection(dialect, connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        return new DbExecutor(connection, transaction, logger);
    }

    /// <summary>
    /// 执行非查询 SQL 命令（INSERT/UPDATE/UPSERT）。
    /// </summary>
    public async Task<int> ExecuteNonQueryAsync(string sql, IReadOnlyList<object?> parameters, CancellationToken ct)
    {
        var dynamicParams = BuildParameters(parameters);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var affected = await _connection.ExecuteAsync(new CommandDefinition(
                sql, dynamicParams, _transaction, cancellationToken: ct)).ConfigureAwait(false);
            stopwatch.Stop();
            // OBS-3：记录脱敏 SQL、影响行数与耗时；参数值不随 SQL 记录（绑定参数，单独传递）。
            _logger?.LogInformation(
                "DB 执行完成（非查询）：影响行数={AffectedRows}，耗时={ElapsedMs}ms，SQL={Sql}",
                affected, stopwatch.ElapsedMilliseconds, sql);
            return affected;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            // 失败时记录 SQL 文本（不含参数值），便于诊断。
            _logger?.LogError(
                ex, "DB 执行失败（非查询）：耗时={ElapsedMs}ms，SQL={Sql}", stopwatch.ElapsedMilliseconds, sql);
            throw;
        }
    }

    /// <summary>
    /// 执行查询并返回 <see cref="DbDataReader"/>，供读节点逐行映射为 <see cref="DataItem"/>。
    /// 调用方必须在释放 <see cref="DbExecutor"/> 前完整读取并释放返回的 reader。
    /// </summary>
    public async Task<DbDataReader> ExecuteReaderAsync(
        string sql,
        IReadOnlyList<object?>? parameters,
        CancellationToken ct,
        int? commandTimeout = null)
    {
        DynamicParameters? dynamicParams = parameters is { Count: > 0 } ? BuildParameters(parameters) : null;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var reader = await _connection.ExecuteReaderAsync(new CommandDefinition(
                sql, dynamicParams, _transaction, commandTimeout: commandTimeout, cancellationToken: ct)).ConfigureAwait(false);
            stopwatch.Stop();
            _logger?.LogInformation(
                "DB 执行完成（查询 reader）：耗时={ElapsedMs}ms，SQL={Sql}", stopwatch.ElapsedMilliseconds, sql);
            return reader;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(
                ex, "DB 执行失败（查询 reader）：耗时={ElapsedMs}ms，SQL={Sql}", stopwatch.ElapsedMilliseconds, sql);
            throw;
        }
    }

    /// <summary>
    /// 执行查询并返回 <see cref="DbDataReader"/>，使用命名参数（<c>@name</c> 占位符）。
    /// 供 dbRead 节点将上游数据以绑定参数形式传入，杜绝将上游值拼接入 SQL 文本导致的注入。
    /// </summary>
    public async Task<DbDataReader> ExecuteReaderAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken ct,
        int? commandTimeout = null)
    {
        DynamicParameters? dynamicParams = parameters is { Count: > 0 } ? BuildNamedParameters(parameters) : null;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var reader = await _connection.ExecuteReaderAsync(new CommandDefinition(
                sql, dynamicParams, _transaction, commandTimeout: commandTimeout, cancellationToken: ct)).ConfigureAwait(false);
            stopwatch.Stop();
            _logger?.LogInformation(
                "DB 执行完成（查询 reader/命名参数）：耗时={ElapsedMs}ms，SQL={Sql}", stopwatch.ElapsedMilliseconds, sql);
            return reader;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(
                ex, "DB 执行失败（查询 reader/命名参数）：耗时={ElapsedMs}ms，SQL={Sql}", stopwatch.ElapsedMilliseconds, sql);
            throw;
        }
    }

    /// <summary>
    /// 执行标量查询（如检查行是否存在）。
    /// </summary>
    /// <remarks>与原始 ADO.NET 行为一致：结果为数据库 NULL 时返回 <see cref="DBNull.Value"/>。</remarks>
    public async Task<object?> ExecuteScalarAsync(string sql, IReadOnlyList<object?> parameters, CancellationToken ct)
    {
        var dynamicParams = BuildParameters(parameters);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await _connection.ExecuteScalarAsync(new CommandDefinition(
                sql, dynamicParams, _transaction, cancellationToken: ct)).ConfigureAwait(false);
            stopwatch.Stop();
            _logger?.LogInformation(
                "DB 执行完成（标量）：耗时={ElapsedMs}ms，SQL={Sql}", stopwatch.ElapsedMilliseconds, sql);
            // Dapper 会把 DBNull 转为 null，这里还原为原始 ADO.NET 契约。
            return result ?? DBNull.Value;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(
                ex, "DB 执行失败（标量）：耗时={ElapsedMs}ms，SQL={Sql}", stopwatch.ElapsedMilliseconds, sql);
            throw;
        }
    }

    /// <summary>
    /// 执行查询并将每行映射为 <typeparamref name="T"/>。
    /// 供未来的读节点（如 DbReaderNode）使用。
    /// </summary>
    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, IReadOnlyList<object?>? parameters, CancellationToken ct)
    {
        DynamicParameters? dynamicParams = parameters is { Count: > 0 } ? BuildParameters(parameters) : null;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await _connection.QueryAsync<T>(new CommandDefinition(
                sql, dynamicParams, _transaction, cancellationToken: ct)).ConfigureAwait(false);
            stopwatch.Stop();
            _logger?.LogInformation(
                "DB 执行完成（查询映射）：行数={RowCount}，耗时={ElapsedMs}ms，SQL={Sql}",
                result.AsList().Count, stopwatch.ElapsedMilliseconds, sql);
            return result.AsList();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(
                ex, "DB 执行失败（查询映射）：耗时={ElapsedMs}ms，SQL={Sql}", stopwatch.ElapsedMilliseconds, sql);
            throw;
        }
    }

    /// <summary>
    /// 将位置参数列表转换为 Dapper 命名参数（@p0、@p1 …）。
    /// 空参数会被 Dapper 自动转换为 DBNull，与原始 ADO.NET 行为一致。
    /// </summary>
    private static DynamicParameters BuildParameters(IReadOnlyList<object?> parameters)
    {
        var dynamicParams = new DynamicParameters();
        for (var i = 0; i < parameters.Count; i++)
        {
            dynamicParams.Add($"@p{i}", parameters[i], null);
        }
        return dynamicParams;
    }

    /// <summary>
    /// 将命名参数映射转换为 Dapper 命名参数（<c>@name</c>）。
    /// 键可带或不带前导 <c>@</c>；空值由 Dapper 自动转换为 DBNull。
    /// </summary>
    private static DynamicParameters BuildNamedParameters(IReadOnlyDictionary<string, object?> parameters)
    {
        var dynamicParams = new DynamicParameters();
        foreach (var (key, value) in parameters)
        {
            var paramName = key.StartsWith("@", StringComparison.Ordinal) ? key : $"@{key}";
            dynamicParams.Add(paramName, value, null);
        }
        return dynamicParams;
    }

    /// <summary>
    /// 提交事务。
    /// </summary>
    public async Task CommitAsync(CancellationToken ct)
    {
        await _transaction.CommitAsync(ct).ConfigureAwait(false);
        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_committed)
        {
            try
            {
                await _transaction.RollbackAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Dispose 阶段的事务回滚失败通常意味着连接已关闭或事务已中止，
                // 记录日志便于排查，但不抛出以避免在析构路径上掩盖原始异常。
                _logger?.LogError(ex, "DbExecutor 回滚事务失败: {Message}", ex.Message);
            }
        }

        await _transaction.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}
