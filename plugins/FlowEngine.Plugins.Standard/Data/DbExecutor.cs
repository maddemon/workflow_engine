using System.Data.Common;

namespace FlowEngine.Plugins.Standard.Data;

/// <summary>
/// 数据库执行器，封装连接/事务生命周期。
/// 调用方负责提供 SQL、参数和逐行值计算。
/// </summary>
public sealed class DbExecutor : IAsyncDisposable
{
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private bool _committed;
    private bool _disposed;

    private DbExecutor(DbConnection connection, DbTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    /// <summary>
    /// 创建并打开数据库连接和事务。
    /// </summary>
    public static async Task<DbExecutor> CreateAsync(DbDialect dialect, string connectionString, CancellationToken ct)
    {
        var connection = DbConnectionFactory.CreateConnection(dialect, connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        return new DbExecutor(connection, transaction);
    }

    /// <summary>
    /// 执行非查询 SQL 命令（INSERT/UPDATE/UPSERT）。
    /// </summary>
    public async Task<int> ExecuteNonQueryAsync(string sql, IReadOnlyList<object?> parameters, CancellationToken ct)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sql;
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"@p{i}";
            parameter.Value = parameters[i] ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行标量查询（如检查行是否存在）。
    /// </summary>
    public async Task<object?> ExecuteScalarAsync(string sql, IReadOnlyList<object?> parameters, CancellationToken ct)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = sql;
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"@p{i}";
            parameter.Value = parameters[i] ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
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
            try { await _transaction.RollbackAsync().ConfigureAwait(false); }
            catch { /* best-effort rollback on dispose */ }
        }

        await _transaction.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}
