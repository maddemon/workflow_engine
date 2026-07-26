using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Storage;
using Moq;
using StackExchange.Redis;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// redis 节点测试。通过 <see cref="RedisNode.DatabaseOverride"/> 注入 <see cref="Mock{IDatabase}"/>，
/// 验证 Set/Get/Del/Expire 行为、缺失连接/键、以及 Redis 异常映射为 RedisError。
/// Connection 直接用 <see cref="CredentialValue"/> 构造；上下文直接 new（参考 SendEmailNodeTests.CreateContext）。
/// </summary>
public sealed class RedisNodeTests
{
    [Fact]
    public async Task ExecuteAsync_SetThenGet_ReturnsConsistentValue()
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.StringSet("k", "v", It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>())).Returns(true);
        db.Setup(d => d.StringGet("k")).Returns((RedisValue)"v");

        var setResult = await Set(db, "k", "v");
        Assert.True(setResult.Success, setResult.Error?.Message);
        Assert.Equal("k", GetString(setResult.Output.Items[0].Data, "key"));
        Assert.True(GetBool(setResult.Output.Items[0].Data, "success"));

        var getResult = await Get(db, "k");
        Assert.True(getResult.Success, getResult.Error?.Message);
        Assert.Equal("k", GetString(getResult.Output.Items[0].Data, "key"));
        Assert.Equal("v", GetString(getResult.Output.Items[0].Data, "value"));
        Assert.True(GetBool(getResult.Output.Items[0].Data, "exists"));
    }

    [Fact]
    public async Task ExecuteAsync_Expire_SetsCorrectTimeSpan()
    {
        var db = new Mock<IDatabase>();
        TimeSpan? captured = null;

        db.Setup(d => d.KeyExpire("k", It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, TimeSpan?, ExpireWhen, CommandFlags>((_, t, _, _) => captured = t)
            .Returns(true);

        var node = new RedisNode
        {
            Connection = RedisCredential(),
            DatabaseOverride = db.Object,
            Operation = RedisOperation.Expire,
            Key = "k",
            Ttl = 60
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.True(GetBool(result.Output.Items[0].Data, "success"));
        Assert.Equal(TimeSpan.FromSeconds(60), captured);
    }

    [Fact]
    public async Task ExecuteAsync_Del_ReturnsDeletedBool()
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.KeyDelete("k")).Returns(true);

        var node = new RedisNode
        {
            Connection = RedisCredential(),
            DatabaseOverride = db.Object,
            Operation = RedisOperation.Del,
            Key = "k"
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.True(GetBool(result.Output.Items[0].Data, "deleted"));
    }

    [Fact]
    public async Task ExecuteAsync_GetOnMissing_ReturnsExistsFalse()
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.StringGet("k")).Returns(RedisValue.Null);

        var result = await Get(db, "k");

        Assert.True(result.Success, result.Error?.Message);
        Assert.False(GetBool(result.Output.Items[0].Data, "exists"));
        Assert.Null(GetString(result.Output.Items[0].Data, "value"));
    }

    [Fact]
    public async Task ExecuteAsync_MissingConnection_ReturnsMissingConnection()
    {
        var node = new RedisNode
        {
            Operation = RedisOperation.Get,
            Key = "k"
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(FlowConstants.ErrorCodes.MissingConnection, result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_MissingKey_ReturnsMissingKey()
    {
        var node = new RedisNode
        {
            Connection = RedisCredential(),
            Operation = RedisOperation.Get,
            Key = ""
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingKey", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_RedisException_ReturnsRedisError()
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.StringGet("k")).Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "boom"));

        var result = await Get(db, "k");

        Assert.False(result.Success);
        Assert.Equal("RedisError", result.Error?.Code);
        Assert.Contains("boom", result.Error?.Message ?? string.Empty);
    }

    [Fact]
    public async Task ExecuteAsync_Set_WithTtl_CapturesTimeSpan()
    {
        var db = new Mock<IDatabase>();
        TimeSpan? captured = null;
        db.Setup(d => d.StringSet("k", "v", It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, When, CommandFlags>((_, _, t, _, _) => captured = t)
            .Returns(true);

        var node = new RedisNode
        {
            Connection = RedisCredential(),
            DatabaseOverride = db.Object,
            Operation = RedisOperation.Set,
            Key = "k",
            Value = "v",
            Ttl = 120
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(TimeSpan.FromSeconds(120), captured);
    }

    private static async Task<NodeExecutionResult> Set(Mock<IDatabase> db, string key, string value)
    {
        var node = new RedisNode
        {
            Connection = RedisCredential(),
            DatabaseOverride = db.Object,
            Operation = RedisOperation.Set,
            Key = key,
            Value = value
        };
        return await ((INodeType)node).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);
    }

    private static async Task<NodeExecutionResult> Get(Mock<IDatabase> db, string key)
    {
        var node = new RedisNode
        {
            Connection = RedisCredential(),
            DatabaseOverride = db.Object,
            Operation = RedisOperation.Get,
            Key = key
        };
        return await ((INodeType)node).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);
    }

    private static bool GetBool(JsonNode? node, string key)
        => node?[key] is JsonValue value && value.TryGetValue<bool>(out var b) && b;

    private static string? GetString(JsonNode? node, string key)
        => node?[key] is JsonValue value ? value.GetValue<string>() : null;

    private static CredentialValue RedisCredential()
    {
        var fields = new Dictionary<string, string>
        {
            ["host"] = "localhost",
            ["port"] = "6379",
            ["password"] = "secret-password", // 测试用占位，绝不输出到日志/异常
            ["db"] = "0"
        };
        return new CredentialValue
        {
            Name = "test-redis",
            Type = "redis",
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
                Id = "redis",
                TypeName = "redis",
                Name = "redis"
            },
            Inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
            {
                [FlowConstants.PortNames.Input] = input
            }
        };
    }
}
