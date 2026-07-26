using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using StackExchange.Redis;

namespace FlowEngine.Plugins.Storage;

/// <summary>
/// Redis 节点。基于 <see cref="StackExchange.Redis"/> 对 Redis 执行简单的字符串读写与键管理操作。
/// 凭据使用内置 <c>redis</c> 类型（字段：host/port/password/db）。
/// </summary>
/// <remarks>
/// 测试可通过 <see cref="DatabaseOverride"/> 注入模拟的 <see cref="IDatabase"/>，从而在不连接真实 Redis 的情况下验证行为。
/// 运行时若未注入 <see cref="DatabaseOverride"/>，则按凭据构造连接并调用
/// <see cref="IConnectionMultiplexer.GetDatabase(int?)"/> 获取数据库。
/// 日志仅记录 key 与 operation，绝不记录 password 等敏感信息。
/// </remarks>
[NodeMeta(TypeName = "redis", DisplayName = "Redis", Category = NodeCategory.Storage, Icon = "redis", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class RedisNode : NodeBase
{
    /// <summary>
    /// 注入的 Redis 数据库（测试用内部接缝）。非空时跳过凭据连接，直接使用该实例。
    /// </summary>
    internal IDatabase? DatabaseOverride { get; set; }

    /// <summary>
    /// Redis 凭据（类型为 <c>redis</c>）。字段：host/port/password/db。密码为 secret，绝不输出到日志或异常。
    /// </summary>
    [Credential("redis")]
    [Description("Redis credential (type: redis). Fields: host/port/password/db.")]
    public CredentialValue? Connection { get; set; }

    /// <summary>
    /// 要执行的操作。默认 <see cref="RedisOperation.Get"/>。
    /// </summary>
    [Description("Operation to perform: Get, Set, Del, or Expire. Default Get.")]
    public RedisOperation Operation { get; set; } = RedisOperation.Get;

    /// <summary>
    /// Redis 键名。所有操作必填。
    /// </summary>
    [Description("Redis key. Required for all operations.")]
    public string? Key { get; set; }

    /// <summary>
    /// 值（仅 <see cref="RedisOperation.Set"/> 使用）。
    /// </summary>
    [Description("Value to store. Used only by the Set operation.")]
    public string? Value { get; set; }

    /// <summary>
    /// 过期时间（秒）。用于 Set 的 TTL 与 Expire 操作；0 或负数表示无过期。
    /// </summary>
    [Description("TTL in seconds for Set/Expire. 0 or negative means no expiry.")]
    public int? Ttl { get; set; }

    /// <inheritdoc />
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        try
        {
            if (Connection is null)
            {
                throw new NodeExecutionException(FlowConstants.ErrorCodes.MissingConnection, "Redis connection credential is required.");
            }

            if (string.IsNullOrWhiteSpace(Key))
            {
                throw new NodeExecutionException("MissingKey", "Redis key is required for all operations.");
            }

            var db = DatabaseOverride ?? await ConnectAsync(Connection, ct).ConfigureAwait(false);

            // 仅记录操作与键名；绝不记录 password 或完整凭据。
            Logger?.LogInformation("redis 操作 {Operation} 作用于键 {Key}。", Operation, Key);

            return Operation switch
            {
                RedisOperation.Set => ExecuteSet(db),
                RedisOperation.Del => ExecuteDel(db),
                RedisOperation.Expire => ExecuteExpire(db),
                _ => ExecuteGet(db)
            };
        }
        catch (RedisConnectionException ex)
        {
            Logger?.LogError(ex, "redis 连接失败（键 {Key}）。", Key);
            throw new NodeExecutionException("RedisError", $"Redis connection failed: {ex.Message}");
        }
        catch (RedisTimeoutException ex)
        {
            Logger?.LogError(ex, "redis 操作超时（键 {Key}）。", Key);
            throw new NodeExecutionException("RedisError", $"Redis operation timed out: {ex.Message}");
        }
        catch (Exception ex) when (ex is not NodeExecutionException)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.UnexpectedError, $"Unexpected error in redis node: {ex.Message}");
        }
    }

    /// <summary>
    /// 按凭据构造连接并获取数据库；连接失败会抛出 <see cref="RedisConnectionException"/>。
    /// </summary>
    private static async Task<IDatabase> ConnectAsync(CredentialValue connection, CancellationToken cancellationToken)
    {
        var options = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            ConnectTimeout = 10000
        };

        if (connection.Fields.TryGetValue("host", out var host) && !string.IsNullOrWhiteSpace(host))
        {
            var port = connection.Fields.TryGetValue("port", out var portStr) && int.TryParse(portStr, out var parsedPort) && parsedPort > 0
                ? parsedPort
                : 6379;
            options.EndPoints.Add(host, port);
        }

        if (connection.Fields.TryGetValue("password", out var password) && !string.IsNullOrWhiteSpace(password))
        {
            options.Password = password;
        }

        if (connection.Fields.TryGetValue("db", out var dbStr) && int.TryParse(dbStr, out var dbIndex) && dbIndex >= 0)
        {
            options.DefaultDatabase = dbIndex;
        }

        var multiplexer = await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
        return multiplexer.GetDatabase();
    }

    /// <summary>
    /// Get：读取字符串值；缺失时返回 <c>exists=false</c>。
    /// </summary>
    private NodeHandlerOutput ExecuteGet(IDatabase db)
    {
        var value = db.StringGet(Key);
        var exists = !value.IsNull;
        return NodeHandlerOutput.Data(new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = new JsonObject
                    {
                        ["key"] = Key,
                        ["value"] = exists ? value.ToString() : null,
                        ["exists"] = exists
                    },
                    Success = true,
                    SourceIndex = 0
                }
            ]
        });
    }

    /// <summary>
    /// Set：写入字符串值（可选 TTL）；返回 <c>success=true</c>。
    /// </summary>
    private NodeHandlerOutput ExecuteSet(IDatabase db)
    {
        var expiry = Ttl.HasValue && Ttl.Value > 0 ? TimeSpan.FromSeconds(Ttl.Value) : (TimeSpan?)null;
        db.StringSet(Key, Value ?? string.Empty, expiry, When.Always, CommandFlags.None);
        return NodeHandlerOutput.Data(new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = new JsonObject
                    {
                        ["key"] = Key,
                        ["success"] = true
                    },
                    Success = true,
                    SourceIndex = 0
                }
            ]
        });
    }

    /// <summary>
    /// Del：删除键；返回 <c>deleted</c> 布尔值。
    /// </summary>
    private NodeHandlerOutput ExecuteDel(IDatabase db)
    {
        var deleted = db.KeyDelete(Key);
        return NodeHandlerOutput.Data(new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = new JsonObject
                    {
                        ["key"] = Key,
                        ["deleted"] = deleted
                    },
                    Success = true,
                    SourceIndex = 0
                }
            ]
        });
    }

    /// <summary>
    /// Expire：设置过期时间；返回 <c>success</c> 布尔值。
    /// </summary>
    private NodeHandlerOutput ExecuteExpire(IDatabase db)
    {
        var expiry = Ttl.HasValue && Ttl.Value > 0 ? TimeSpan.FromSeconds(Ttl.Value) : (TimeSpan?)null;
        var success = db.KeyExpire(Key, expiry, ExpireWhen.Always, CommandFlags.None);
        return NodeHandlerOutput.Data(new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = new JsonObject
                    {
                        ["key"] = Key,
                        ["success"] = success
                    },
                    Success = true,
                    SourceIndex = 0
                }
            ]
        });
    }
}

/// <summary>
/// Redis 节点支持的操作类型。
/// </summary>
public enum RedisOperation
{
    /// <summary>读取键的字符串值。</summary>
    [Description("Get")]
    Get,

    /// <summary>写入键的字符串值（可选 TTL）。</summary>
    [Description("Set")]
    Set,

    /// <summary>删除键。</summary>
    [Description("Del")]
    Del,

    /// <summary>设置键的过期时间。</summary>
    [Description("Expire")]
    Expire
}
