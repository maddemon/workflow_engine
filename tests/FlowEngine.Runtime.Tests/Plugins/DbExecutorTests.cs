using FlowEngine.Plugins.Standard.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// 验证 <see cref="DbExecutor"/> 在 Dapper 重构后的行为：
/// 参数命名（@p{i}）、NULL 处理、事务语义，以及新增的 <see cref="DbExecutor.QueryAsync{T}"/>。
/// </summary>
public sealed class DbExecutorTests
{
    private static string UniqueConnectionString() => $"Data Source=dbexec_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

    private static async Task<DbExecutor> CreateExecutorAsync(string connectionString)
        => await DbExecutor.CreateAsync(DbDialect.SQLite, connectionString, CancellationToken.None);

    private static async Task CreateTableAsync(DbExecutor executor)
    {
        await executor.ExecuteNonQueryAsync(
            "CREATE TABLE users (\"id\" INTEGER PRIMARY KEY, \"name\" TEXT NOT NULL, \"email\" TEXT)",
            [],
            CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_Insert_ThenCommit_PersistsRow()
    {
        var connectionString = UniqueConnectionString();
        await using var executor = await CreateExecutorAsync(connectionString);
        await CreateTableAsync(executor);

        var affected = await executor.ExecuteNonQueryAsync(
            "INSERT INTO users (\"id\", \"name\", \"email\") VALUES (@p0, @p1, @p2)",
            [1, "Alice", "alice@example.com"],
            CancellationToken.None);

        Assert.Equal(1, affected);

        // 跨连接验证（共享内存）已提交数据。
        await executor.CommitAsync(CancellationToken.None);
        await using var verify = new SqliteConnection(connectionString);
        await verify.OpenAsync();
        using var verifyCommand = verify.CreateCommand();
        verifyCommand.CommandText = "SELECT \"name\" FROM users WHERE \"id\" = 1";
        var name = await verifyCommand.ExecuteScalarAsync();
        Assert.Equal("Alice", name);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_NullParameter_HandledAsDbNull()
    {
        var connectionString = UniqueConnectionString();
        await using var executor = await CreateExecutorAsync(connectionString);
        await CreateTableAsync(executor);

        var affected = await executor.ExecuteNonQueryAsync(
            "INSERT INTO users (\"id\", \"name\", \"email\") VALUES (@p0, @p1, @p2)",
            [2, "Bob", null],
            CancellationToken.None);

        Assert.Equal(1, affected);

        var email = await executor.ExecuteScalarAsync(
            "SELECT \"email\" FROM users WHERE \"id\" = @p0",
            [2],
            CancellationToken.None);

        Assert.Equal(DBNull.Value, email);
        await executor.CommitAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteScalarAsync_ReturnsAggregateValue()
    {
        var connectionString = UniqueConnectionString();
        await using var executor = await CreateExecutorAsync(connectionString);
        await CreateTableAsync(executor);

        await executor.ExecuteNonQueryAsync(
            "INSERT INTO users (\"id\", \"name\", \"email\") VALUES (@p0, @p1, @p2)",
            [3, "Carol", "carol@example.com"],
            CancellationToken.None);

        var count = await executor.ExecuteScalarAsync(
            "SELECT COUNT(*) FROM users",
            [],
            CancellationToken.None);

        Assert.Equal(1L, count);
        await executor.CommitAsync(CancellationToken.None);
    }

    [Fact]
    public async Task QueryAsync_ReturnsMappedRows()
    {
        var connectionString = UniqueConnectionString();
        await using var executor = await CreateExecutorAsync(connectionString);
        await CreateTableAsync(executor);

        await executor.ExecuteNonQueryAsync(
            "INSERT INTO users (\"id\", \"name\", \"email\") VALUES (@p0, @p1, @p2)",
            [4, "Dave", "dave@example.com"],
            CancellationToken.None);

        var rows = await executor.QueryAsync<UserRow>(
            "SELECT \"id\" AS Id, \"name\" AS Name, \"email\" AS Email FROM users ORDER BY \"id\"",
            null,
            CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(4, row.Id);
        Assert.Equal("Dave", row.Name);
        Assert.Equal("dave@example.com", row.Email);
        await executor.CommitAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RollbackAsync_DisposeWithoutCommit_DiscardsChanges()
    {
        var connectionString = UniqueConnectionString();

        // 保持一条独立连接，使共享内存数据库在 executor 释放后仍然存在。
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();

        // 先提交建表，保证后续验证时表一定存在。
        await using (var setup = await CreateExecutorAsync(connectionString))
        {
            await CreateTableAsync(setup);
            await setup.CommitAsync(CancellationToken.None);
        }

        // 未提交即释放，DisposeAsync 应触发回滚，插入行被丢弃。
        await using (var executor = await CreateExecutorAsync(connectionString))
        {
            await executor.ExecuteNonQueryAsync(
                "INSERT INTO users (\"id\", \"name\", \"email\") VALUES (@p0, @p1, @p2)",
                [5, "Eve", "eve@example.com"],
                CancellationToken.None);
            // 故意不提交。
        }

        using var countCommand = keepAlive.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM users";
        var count = await countCommand.ExecuteScalarAsync();
        Assert.Equal(0L, count);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_LogsSanitizedSqlWithoutParameterValues()
    {
        // OBS-3：DB 节点执行应记录 SQL 文本（含 @p{N} 占位符）与影响行数/耗时，
        // 但绝不记录绑定参数值（凭据/令牌等敏感数据）。
        var logger = new CaptureLogger();
        var connectionString = UniqueConnectionString();
        await using var executor = await DbExecutor.CreateAsync(DbDialect.SQLite, connectionString, CancellationToken.None, logger);
        await CreateTableAsync(executor);

        var secret = "s3cr3t-token-value";
        await executor.ExecuteNonQueryAsync(
            "INSERT INTO users (\"id\", \"name\", \"email\") VALUES (@p0, @p1, @p2)",
            [1, "Alice", secret],
            CancellationToken.None);
        await executor.CommitAsync(CancellationToken.None);

        // 参数明文值绝不得出现在任何日志中（凭据/令牌等敏感数据）。
        Assert.DoesNotContain(secret, string.Join("\n", logger.Messages));

        var sqlLog = logger.Messages.First(m => m.Contains("INSERT INTO users"));
        Assert.Contains("INSERT INTO users", sqlLog);
        // 脱敏后的 SQL 文本应保留参数占位符。
        Assert.Contains("@p2", sqlLog);
    }

    private sealed class CaptureLogger : ILogger
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class UserRow
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
    }
}
