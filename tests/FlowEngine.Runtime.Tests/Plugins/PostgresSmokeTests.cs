using System.Data.Common;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Plugins.Standard;
using FlowEngine.Plugins.Standard.Data;
using Npgsql;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// PostgreSQL 冒烟测试：用真实连接串驱动 dbUpsert / dbRead 节点，验证 Postgres 支持。
/// 属集成冒烟测试，依赖外部 PostgreSQL 实例；当目标实例不可达时（如 CI 未提供 Postgres
/// 服务容器）通过 <see cref="Assert.Skip"/> 优雅跳过，而非让测试套件整体失败。
/// </summary>
public sealed class PostgresSmokeTests
{
    private const string Host = "127.0.0.1";
    private const int Port = 5432;
    private const string Database = "postgres";
    private const string UserId = "postgres";
    private const string Password = "123456";
    private const string Table = "flow_engine_pg_smoke";

    [Fact]
    public async Task DbUpsertAndDbRead_AgainstLivePostgres_Work()
    {
        var connectionString = BuildConnectionString();

        // 先探测可达性：PostgreSQL 不可用时跳过（避免 CI 无 Postgres 服务时整体失败）。
        NpgsqlConnection? setupConn = null;
        try
        {
            setupConn = new NpgsqlConnection(connectionString);
            await setupConn.OpenAsync();
        }
        catch (Exception ex) when (ex is NpgsqlException or System.Net.Sockets.SocketException or TimeoutException or OperationCanceledException)
        {
            Assert.Skip($"未检测到可用的 PostgreSQL（{Host}:{Port}），跳过 Postgres 冒烟测试：{ex.Message}");
        }

        await using (setupConn)
        {
            try
            {
            await ExecuteNonQueryAsync(setupConn, $"DROP TABLE IF EXISTS \"{Table}\"");
            await ExecuteNonQueryAsync(setupConn,
                $"CREATE TABLE \"{Table}\" (\"id\" INTEGER PRIMARY KEY, \"name\" TEXT NOT NULL, \"email\" TEXT)");

            var credential = PostgresCredential();

            // 1) insert
            var insertNode = new DbUpsertNode
            {
                Connection = credential,
                Table = Table,
                Mode = DbUpsertMode.Upsert,
                KeyColumns = "id",
                Columns = ToScriptColumns(new Dictionary<string, string>
                {
                    ["id"] = "$json.id",
                    ["name"] = "$json.name",
                    ["email"] = "$json.email"
                })
            };

            var insertResult = await ((INodeType)insertNode).ExecuteAsync(
                CreateContext(new DataBatch
                {
                    Items = [ new DataItem { Data = CreateUser(1, "Alice", "alice@example.com"), Success = true } ]
                }),
                CancellationToken.None);

            Assert.True(insertResult.Success, insertResult.Error?.Message);
            Assert.Equal(1, GetInt(insertResult.Output.Items[0].Data, "inserted"));
            Assert.Equal(0, GetInt(insertResult.Output.Items[0].Data, "updated"));

            // 2) read back
            var readNode = new DbReadNode
            {
                Connection = credential,
                Sql = Literal($"SELECT \"id\", \"name\", \"email\" FROM \"{Table}\" ORDER BY \"id\"")
            };

            var readResult = await ((INodeType)readNode).ExecuteAsync(
                CreateContext(new DataBatch()), CancellationToken.None);

            Assert.True(readResult.Success, readResult.Error?.Message);
            Assert.Single(readResult.Output.Items);
            Assert.Equal(1, GetInt(readResult.Output.Items[0].Data, "id"));
            Assert.Equal("Alice", GetString(readResult.Output.Items[0].Data, "name"));
            Assert.Equal("alice@example.com", GetString(readResult.Output.Items[0].Data, "email"));

            // 3) upsert again -> update existing row
            var updateNode = new DbUpsertNode
            {
                Connection = credential,
                Table = Table,
                Mode = DbUpsertMode.Upsert,
                KeyColumns = "id",
                Columns = ToScriptColumns(new Dictionary<string, string>
                {
                    ["id"] = "$json.id",
                    ["name"] = "$json.name",
                    ["email"] = "$json.email"
                })
            };

            var updateResult = await ((INodeType)updateNode).ExecuteAsync(
                CreateContext(new DataBatch
                {
                    Items = [ new DataItem { Data = CreateUser(1, "Alice Smith", "alice.smith@example.com"), Success = true } ]
                }),
                CancellationToken.None);

            Assert.True(updateResult.Success, updateResult.Error?.Message);
            Assert.Equal(0, GetInt(updateResult.Output.Items[0].Data, "inserted"));
            Assert.Equal(1, GetInt(updateResult.Output.Items[0].Data, "updated"));

            // 4) verify the update took effect (ON CONFLICT path)
            var read2 = await ((INodeType)readNode).ExecuteAsync(
                CreateContext(new DataBatch()), CancellationToken.None);

            Assert.True(read2.Success, read2.Error?.Message);
            Assert.Single(read2.Output.Items);
            Assert.Equal("Alice Smith", GetString(read2.Output.Items[0].Data, "name"));
            Assert.Equal("alice.smith@example.com", GetString(read2.Output.Items[0].Data, "email"));
            }
        finally
        {
            await ExecuteNonQueryAsync(setupConn, $"DROP TABLE IF EXISTS \"{Table}\"");
        }
        }
    }

    private static string BuildConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Database,
            Username = UserId,
            Password = Password,
            Timeout = 3
        };
        return builder.ConnectionString;
    }

    private static CredentialValue PostgresCredential()
    {
        return new CredentialValue
        {
            Name = "pg-local",
            Type = "database",
            Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [FlowConstants.CredentialFields.DbType] = "postgresql",
                ["host"] = Host,
                ["port"] = Port.ToString(),
                ["database"] = Database,
                ["userid"] = UserId,
                ["password"] = Password
            }
        };
    }

    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static JsonObject CreateUser(int id, string name, string? email) =>
        new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["email"] = email is null ? null : JsonValue.Create(email)
        };

    private static Script Literal(string sql) =>
        (Script)$"\"{sql.Replace("\"", "\\\"")}\"";

    private static int GetInt(JsonNode? node, string key)
    {
        if (node?[key] is not JsonValue value) return 0;
        if (value.TryGetValue<int>(out var i)) return i;
        if (value.TryGetValue<long>(out var l)) return (int)l;
        return 0;
    }

    private static string? GetString(JsonNode? node, string key) =>
        node?[key] is JsonValue value ? value.GetValue<string>() : null;

    private static Dictionary<string, Script> ToScriptColumns(Dictionary<string, string> columns) =>
        columns.ToDictionary(c => c.Key, c => (Script)c.Value, StringComparer.OrdinalIgnoreCase);

    private static NodeExecutionContext CreateContext(DataBatch input)
    {
        return new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "test" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition { Id = "pg", TypeName = "pg", Name = "pg" },
            Inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
            {
                [FlowConstants.PortNames.Input] = input
            }
        };
    }
}
