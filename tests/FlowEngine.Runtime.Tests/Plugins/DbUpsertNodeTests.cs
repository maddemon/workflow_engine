using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Plugins.Standard;
using FlowEngine.Plugins.Standard.Data;
using Microsoft.Data.Sqlite;

namespace FlowEngine.Runtime.Tests.Plugins;

public sealed class DbUpsertNodeTests
{
    [Fact]
    public async Task ExecuteAsync_Upsert_InsertThenUpdate()
    {
        const string connectionString = "Data Source=shared_upsert;Mode=Memory;Cache=Shared";
        using var holder = CreateSharedMemoryConnection(connectionString);
        await CreateUsersTableAsync(holder);

        var node = CreateNode(connectionString, "upsert", "id", new Dictionary<string, string>
        {
            ["id"] = "$json.id",
            ["name"] = "$json.name",
            ["email"] = "$json.email"
        });

        var first = await node.ExecuteAsync(
            CreateContext(new DataBatch
            {
                Items =
                [
                    new DataItem { Data = CreateUser(1, "Alice", "alice@example.com"), Success = true }
                ]
            }),
            CancellationToken.None);

        Assert.True(first.Success, first.Error?.Message);
        var firstData = first.Output.Items[0].Data;
        Assert.Equal(1, GetInt(firstData, "affectedRows"));
        Assert.Equal(1, GetInt(firstData, "inserted"));
        Assert.Equal(0, GetInt(firstData, "updated"));

        var second = await node.ExecuteAsync(
            CreateContext(new DataBatch
            {
                Items =
                [
                    new DataItem { Data = CreateUser(1, "Alice Smith", "alice.smith@example.com"), Success = true }
                ]
            }),
            CancellationToken.None);

        Assert.True(second.Success, second.Error?.Message);
        var secondData = second.Output.Items[0].Data;
        Assert.Equal(1, GetInt(secondData, "affectedRows"));
        Assert.Equal(0, GetInt(secondData, "inserted"));
        Assert.Equal(1, GetInt(secondData, "updated"));

        await using var verify = new SqliteConnection(connectionString);
        await verify.OpenAsync();
        using var command = verify.CreateCommand();
        command.CommandText = "SELECT \"name\", \"email\" FROM users WHERE \"id\" = 1";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Alice Smith", reader.GetString(0));
        Assert.Equal("alice.smith@example.com", reader.GetString(1));
    }

    [Fact]
    public async Task ExecuteAsync_Insert_Mode()
    {
        const string connectionString = "Data Source=shared_insert;Mode=Memory;Cache=Shared";
        using var holder = CreateSharedMemoryConnection(connectionString);
        await CreateUsersTableAsync(holder);

        var node = CreateNode(connectionString, "insert", "", new Dictionary<string, string>
        {
            ["id"] = "$json.id",
            ["name"] = "$json.name",
            ["email"] = "$json.email"
        });

        var result = await node.ExecuteAsync(
            CreateContext(new DataBatch
            {
                Items =
                [
                    new DataItem { Data = CreateUser(2, "Bob", "bob@example.com"), Success = true }
                ]
            }),
            CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = result.Output.Items[0].Data;
        Assert.Equal(1, GetInt(data, "affectedRows"));
        Assert.Equal(1, GetInt(data, "inserted"));
        Assert.Equal(0, GetInt(data, "updated"));

        await using var verify = new SqliteConnection(connectionString);
        await verify.OpenAsync();
        using var command = verify.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM users";
        var count = await command.ExecuteScalarAsync();
        Assert.Equal(1L, count);
    }

    [Fact]
    public async Task ExecuteAsync_Update_Mode()
    {
        const string connectionString = "Data Source=shared_update;Mode=Memory;Cache=Shared";
        using var holder = CreateSharedMemoryConnection(connectionString);
        await CreateUsersTableAsync(holder);
        await InsertSeedRowAsync(holder, 3, "Carol", "carol@example.com");

        var node = CreateNode(connectionString, "update", "id", new Dictionary<string, string>
        {
            ["id"] = "$json.id",
            ["name"] = "$json.name"
        });

        var result = await node.ExecuteAsync(
            CreateContext(new DataBatch
            {
                Items =
                [
                    new DataItem { Data = CreateUser(3, "Carol Updated", null), Success = true }
                ]
            }),
            CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = result.Output.Items[0].Data;
        Assert.Equal(1, GetInt(data, "affectedRows"));
        Assert.Equal(0, GetInt(data, "inserted"));
        Assert.Equal(1, GetInt(data, "updated"));

        await using var verify = new SqliteConnection(connectionString);
        await verify.OpenAsync();
        using var command = verify.CreateCommand();
        command.CommandText = "SELECT \"name\" FROM users WHERE \"id\" = 3";
        var name = await command.ExecuteScalarAsync();
        Assert.Equal("Carol Updated", name);
    }

    [Fact]
    public async Task ExecuteAsync_ParameterizedValues_PreventSqlInjection()
    {
        const string connectionString = "Data Source=shared_injection;Mode=Memory;Cache=Shared";
        using var holder = CreateSharedMemoryConnection(connectionString);
        await CreateUsersTableAsync(holder);

        var malicious = "'; DROP TABLE users; --";
        var node = CreateNode(connectionString, "insert", "", new Dictionary<string, string>
        {
            ["id"] = "$json.id",
            ["name"] = "$json.name",
            ["email"] = "$json.email"
        });

        var result = await node.ExecuteAsync(
            CreateContext(new DataBatch
            {
                Items =
                [
                    new DataItem { Data = CreateUser(4, malicious, "x@example.com"), Success = true }
                ]
            }),
            CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);

        await using var verify = new SqliteConnection(connectionString);
        await verify.OpenAsync();
        using var command = verify.CreateCommand();
        command.CommandText = "SELECT \"name\" FROM users WHERE \"id\" = 4";
        var name = await command.ExecuteScalarAsync();
        Assert.Equal(malicious, name);

        using var tableCheck = verify.CreateCommand();
        tableCheck.CommandText = "SELECT COUNT(*) FROM users";
        var count = await tableCheck.ExecuteScalarAsync();
        Assert.Equal(1L, count);
    }

    [Theory]
    [InlineData("Host=localhost;Database=test;Username=user;Password=pass", DbDialect.PostgreSQL)]
    [InlineData("Server=localhost;Database=test;Uid=user;Pwd=pass", DbDialect.MySQL)]
    [InlineData("Server=localhost;Database=test;User Id=user;Password=pass;Encrypt=false", DbDialect.SqlServer)]
    [InlineData("Data Source=:memory:", DbDialect.SQLite)]
    public void ResolveDialect_FromConnectionString(string connectionString, DbDialect expected)
    {
        var actual = DbDialectResolver.Resolve(null, connectionString);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("postgresql", DbDialect.PostgreSQL)]
    [InlineData("mysql", DbDialect.MySQL)]
    [InlineData("sqlserver", DbDialect.SqlServer)]
    [InlineData("sqlite", DbDialect.SQLite)]
    public void ResolveDialect_FromExplicitValue(string dialect, DbDialect expected)
    {
        var actual = DbDialectResolver.Resolve(dialect, "ignored");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void PostgresSqlGenerator_BuildUpsertSql_ContainsExpectedKeywords()
    {
        var generator = new PostgresSqlGenerator();
        var sql = generator.BuildUpsertSql("users", ["id", "name"], ["id"]);
        Assert.Contains("INSERT INTO", sql);
        Assert.Contains("ON CONFLICT", sql);
        Assert.Contains("DO UPDATE SET", sql);
        Assert.Contains("\"users\"", sql);
        Assert.Contains("@p0", sql);
        Assert.Contains("EXCLUDED", sql);
    }

    [Fact]
    public void MySqlSqlGenerator_BuildUpsertSql_ContainsExpectedKeywords()
    {
        var generator = new MySqlSqlGenerator();
        var sql = generator.BuildUpsertSql("users", ["id", "name"], ["id"]);
        Assert.Contains("INSERT INTO", sql);
        Assert.Contains("ON DUPLICATE KEY UPDATE", sql);
        Assert.Contains("`users`", sql);
        Assert.Contains("AS new", sql);
        Assert.Contains("new.`name`", sql);
    }

    [Fact]
    public void SqlServerSqlGenerator_BuildUpsertSql_ContainsExpectedKeywords()
    {
        var generator = new SqlServerSqlGenerator();
        var sql = generator.BuildUpsertSql("users", ["id", "name"], ["id"]);
        Assert.Contains("MERGE INTO", sql);
        Assert.Contains("USING (VALUES", sql);
        Assert.Contains("WHEN MATCHED THEN UPDATE SET", sql);
        Assert.Contains("WHEN NOT MATCHED THEN INSERT", sql);
        Assert.Contains("[users]", sql);
    }

    [Fact]
    public void SQLiteSqlGenerator_BuildUpsertSql_ContainsExpectedKeywords()
    {
        var generator = new SQLiteSqlGenerator();
        var sql = generator.BuildUpsertSql("users", ["id", "name"], ["id"]);
        Assert.Contains("INSERT INTO", sql);
        Assert.Contains("ON CONFLICT", sql);
        Assert.Contains("DO UPDATE SET", sql);
        Assert.Contains("\"users\"", sql);
        Assert.Contains("excluded", sql);
    }

    [Fact]
    public async Task ExecuteAsync_ResolvedConnection_UsesValue()
    {
        const string connectionString = "Data Source=shared_expr_eval;Mode=Memory;Cache=Shared";
        using var holder = CreateSharedMemoryConnection(connectionString);
        await CreateUsersTableAsync(holder);

        var node = new DbUpsertNode
        {
            Connection = ResolvedConnection(connectionString),
            Table = "users",
            Mode = "insert",
            Columns = ToScriptColumns(new Dictionary<string, string>
            {
                ["id"] = "$json.id",
                ["name"] = "$json.name",
                ["email"] = "$json.email"
            })
        };

        var result = await node.ExecuteAsync(
            CreateContext(new DataBatch
            {
                Items =
                [
                    new DataItem { Data = CreateUser(11, "ExprEval", "expr@example.com"), Success = true }
                ]
            }),
            CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = result.Output.Items[0].Data;
        Assert.Equal(1, GetInt(data, "affectedRows"));
        Assert.Equal(1, GetInt(data, "inserted"));
    }

    [Fact]
    public async Task ExecuteAsync_MissingConnection_ReturnsError()
    {
        var node = new DbUpsertNode
        {
            Table = "users",
            Mode = "insert",
            Columns = ToScriptColumns(new Dictionary<string, string> { ["id"] = "$json.id" })
        };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingConnection", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_MissingTable_ReturnsError()
    {
        var node = new DbUpsertNode
        {
            Connection = ResolvedConnection("Data Source=:memory:"),
            Mode = "insert",
            Columns = ToScriptColumns(new Dictionary<string, string> { ["id"] = "$json.id" })
        };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingTable", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_MissingColumns_ReturnsError()
    {
        var node = new DbUpsertNode
        {
            Connection = ResolvedConnection("Data Source=:memory:"),
            Table = "users",
            Mode = "insert"
        };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingColumns", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidTable_ReturnsError()
    {
        var node = new DbUpsertNode
        {
            Connection = ResolvedConnection("Data Source=:memory:"),
            Table = "users; DROP TABLE users--",
            Mode = "insert",
            Columns = ToScriptColumns(new Dictionary<string, string> { ["id"] = "$json.id" })
        };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("InvalidTable", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidColumn_ReturnsError()
    {
        var node = new DbUpsertNode
        {
            Connection = ResolvedConnection("Data Source=:memory:"),
            Table = "users",
            Mode = "insert",
            Columns = ToScriptColumns(new Dictionary<string, string>
            {
                ["id"] = "$json.id",
                ["name; DROP TABLE users--"] = "$json.name"
            })
        };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("InvalidColumn", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidKeyColumn_ReturnsError()
    {
        var node = new DbUpsertNode
        {
            Connection = ResolvedConnection("Data Source=:memory:"),
            Table = "users",
            Mode = "upsert",
            KeyColumns = "id; DROP TABLE users--",
            Columns = ToScriptColumns(new Dictionary<string, string>
            {
                ["id"] = "$json.id",
                ["name"] = "$json.name"
            })
        };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("InvalidKeyColumn", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_MissingKeyColumn_ReturnsError()
    {
        var node = new DbUpsertNode
        {
            Connection = ResolvedConnection("Data Source=:memory:"),
            Table = "users",
            Mode = "upsert",
            KeyColumns = "missing",
            Columns = ToScriptColumns(new Dictionary<string, string>
            {
                ["id"] = "$json.id",
                ["name"] = "$json.name"
            })
        };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingKeyColumn", result.Error?.Code);
    }

    private static int GetInt(JsonNode? node, string key)
    {
        return node?[key] is JsonValue value ? value.GetValue<int>() : 0;
    }

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

    private static JsonObject CreateUser(int id, string name, string? email)
    {
        return new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["email"] = email is null ? null : JsonValue.Create(email)
        };
    }

    private static DbUpsertNode CreateNode(
        string connectionString,
        string mode,
        string keyColumns,
        Dictionary<string, string> columns,
        string? dialect = null)
    {
        return new DbUpsertNode
        {
            Connection = ResolvedConnection(connectionString),
            Table = "users",
            Mode = mode,
            KeyColumns = keyColumns,
            Columns = ToScriptColumns(columns),
            Dialect = dialect
        };
    }

    private static Script ResolvedConnection(string connectionString)
    {
        return new Script
        {
            Source = $"'{connectionString}'",
            Language = ScriptLanguage.JavaScript,
            ReturnType = ScriptReturnType.String
        }.WithResolvedValue(JsonValue.Create(connectionString));
    }

    private static Dictionary<string, Script> ToScriptColumns(Dictionary<string, string> columns)
    {
        return columns.ToDictionary(
            c => c.Key,
            c => (Script)c.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static NodeExecutionContext CreateContext(DataBatch input, IReadOnlyDictionary<string, object>? resolvedParameters = null)
    {
        var nodeId = Guid.NewGuid();
        return new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "test" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = nodeId,
                TypeName = "dbUpsert",
                Name = "dbUpsert"
            },
            Inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
            {
                [FlowConstants.PortNames.Input] = input
            },
            ResolvedParameters = resolvedParameters ?? new Dictionary<string, object>()
        };
    }
}
