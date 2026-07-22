using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Scripting;
using FlowEngine.Plugins.Standard;
using Microsoft.Data.Sqlite;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// dbRead 节点测试：覆盖正常查询、空结果、缺连接、非 SELECT 只读拦截、SQL 语法错误。
/// </summary>
public sealed class DbReadNodeTests
{
    [Fact]
    public async Task ExecuteAsync_Select_ReturnsRowsAsDataItems()
    {
        const string connectionString = "Data Source=shared_read_select;Mode=Memory;Cache=Shared";
        using var holder = CreateSharedMemoryConnection(connectionString);
        await CreateUsersTableAsync(holder);
        await InsertSeedRowAsync(holder, 1, "Alice", "alice@example.com");
        await InsertSeedRowAsync(holder, 2, "Bob", null);

        var node = CreateNode(connectionString, "SELECT \"id\", \"name\", \"email\" FROM users ORDER BY \"id\"");

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
        Assert.Equal(1, GetInt(result.Output.Items[0].Data, "id"));
        Assert.Equal("Alice", GetString(result.Output.Items[0].Data, "name"));
        Assert.Equal("alice@example.com", GetString(result.Output.Items[0].Data, "email"));
        Assert.Equal(2, GetInt(result.Output.Items[1].Data, "id"));
        Assert.Equal("Bob", GetString(result.Output.Items[1].Data, "name"));
        // NULL 列不应导致崩溃；读取为空（缺失键或 JSON null 均视为空）。
        Assert.Null(GetString(result.Output.Items[1].Data, "email"));
    }

    [Fact]
    public async Task ExecuteAsync_EmptyResult_ReturnsEmptyBatch()
    {
        const string connectionString = "Data Source=shared_read_empty;Mode=Memory;Cache=Shared";
        using var holder = CreateSharedMemoryConnection(connectionString);
        await CreateUsersTableAsync(holder);

        var node = CreateNode(connectionString, "SELECT * FROM users WHERE 1 = 0");

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Empty(result.Output.Items);
    }

    [Fact]
    public async Task ExecuteAsync_MissingConnection_ReturnsError()
    {
        var node = new DbReadNode
        {
            Sql = Literal("SELECT 1")
        };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingConnection", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_NonSelect_RejectedAsReadOnlyViolation()
    {
        const string connectionString = "Data Source=shared_read_violation;Mode=Memory;Cache=Shared";
        using var holder = CreateSharedMemoryConnection(connectionString);
        await CreateUsersTableAsync(holder);

        var node = CreateNode(connectionString, "DELETE FROM users");

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ReadOnlyViolation", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_WithStatement_Allowed()
    {
        const string connectionString = "Data Source=shared_read_with;Mode=Memory;Cache=Shared";
        using var holder = CreateSharedMemoryConnection(connectionString);
        await CreateUsersTableAsync(holder);
        await InsertSeedRowAsync(holder, 7, "Carol", "carol@example.com");

        var node = CreateNode(connectionString, "WITH cte AS (SELECT \"name\" FROM users WHERE \"id\" = 7) SELECT * FROM cte");

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
        Assert.Equal("Carol", GetString(result.Output.Items[0].Data, "name"));
    }

    [Fact]
    public async Task ExecuteAsync_SqlSyntaxError_ReturnsDbErrorNotThrown()
    {
        const string connectionString = "Data Source=shared_read_syntax;Mode=Memory;Cache=Shared";
        using var holder = CreateSharedMemoryConnection(connectionString);
        await CreateUsersTableAsync(holder);

        var node = CreateNode(connectionString, "SELECT * FROM nonexistent_table_xyz");

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("DbError", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_JsonInjection_EvaluatedPerItem()
    {
        const string connectionString = "Data Source=shared_read_inject;Mode=Memory;Cache=Shared";
        using var holder = CreateSharedMemoryConnection(connectionString);

        // Sql 为 JS 表达式：将 $json.greeting 拼接到 SQL 字符串中（注意此处不是 Literal，而是保留 JS 拼接语义）。
        var node = new DbReadNode
        {
            Connection = ResolvedConnection(connectionString),
            Sql = (Script)@"""SELECT '"" + $json.greeting + ""' AS msg"""
        };

        var result = await node.ExecuteAsync(
            CreateContext(new DataBatch
            {
                Items =
                [
                    new DataItem { Data = new JsonObject { ["greeting"] = "hello" }, Success = true }
                ]
            }),
            CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
        Assert.Equal("hello", GetString(result.Output.Items[0].Data, "msg"));
    }

    private static long GetInt(JsonNode? node, string key)
        => node?[key] is JsonValue value ? value.GetValue<long>() : 0;

    private static string? GetString(JsonNode? node, string key)
        => node?[key] is JsonValue value ? value.GetValue<string>() : null;

    private static Script Literal(string sql)
        => (Script)$"\"{sql.Replace("\"", "\\\"")}\"";

    private static SqliteConnection CreateSharedMemoryConnection(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static async Task CreateUsersTableAsync(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE users (\"id\" INTEGER PRIMARY KEY, \"name\" TEXT NOT NULL, \"email\" TEXT)";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertSeedRowAsync(SqliteConnection connection, int id, string name, string? email)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO users (\"id\", \"name\", \"email\") VALUES (@id, @name, @email)";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@email", email ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static DbReadNode CreateNode(string connectionString, string sql)
        => new DbReadNode
        {
            Connection = ResolvedConnection(connectionString),
            Sql = Literal(sql)
        };

    private static CredentialValue ResolvedConnection(string sqliteConnectionString)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in sqliteConnectionString.Split(';'))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0].Trim().Length > 0)
            {
                dict[pair[0].Trim()] = pair[1].Trim();
            }
        }

        var fields = new Dictionary<string, string>
        {
            [FlowConstants.CredentialFields.DbType] = "sqlite"
        };

        if (dict.TryGetValue("Data Source", out var dataSource)) fields["dataSource"] = dataSource;
        if (dict.TryGetValue("Mode", out var mode)) fields["mode"] = mode;
        if (dict.TryGetValue("Cache", out var cache)) fields["cache"] = cache;

        return new CredentialValue
        {
            Name = "test",
            Type = "database",
            Fields = fields
        };
    }

    private static NodeExecutionContext CreateContext(DataBatch input)
    {
        return new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "test" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = "dbRead",
                TypeName = "dbRead",
                Name = "dbRead"
            },
            Inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
            {
                [FlowConstants.PortNames.Input] = input
            }
        };
    }
}
