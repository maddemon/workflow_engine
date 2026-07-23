using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
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
public sealed class RedisNode : INodeType
{
    /// <summary>
    /// 注入的 Redis 数据库（测试用内部接缝）。非空时跳过凭据连接，直接使用该实例。
    /// </summary>
    internal IDatabase? DatabaseOverride { get; set; }

    /// <inheritdoc />
    public string TypeName => "redis";

    /// <inheritdoc />
    public string DisplayName => "Redis";

    /// <inheritdoc />
    public string Category => "Storage";

    /// <inheritdoc />
    public string Icon => "redis";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

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
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (Connection is null)
            {
                return context.ErrorResult(FlowConstants.ErrorCodes.MissingConnection, "Redis connection credential is required.");
            }

            if (string.IsNullOrWhiteSpace(Key))
            {
                return context.ErrorResult("MissingKey", "Redis key is required for all operations.");
            }

            var db = DatabaseOverride ?? await ConnectAsync(Connection, cancellationToken).ConfigureAwait(false);

            // 仅记录操作与键名；绝不记录 password 或完整凭据。
            context.Logger?.LogInformation("redis 操作 {Operation} 作用于键 {Key}。", Operation, Key);

            return Operation switch
            {
                RedisOperation.Set => ExecuteSet(db, context),
                RedisOperation.Del => ExecuteDel(db, context),
                RedisOperation.Expire => ExecuteExpire(db, context),
                _ => ExecuteGet(db, context)
            };
        }
        catch (RedisConnectionException ex)
        {
            context.Logger?.LogError(ex, "redis 连接失败（键 {Key}）。", Key);
            return context.ErrorResult("RedisError", $"Redis connection failed: {ex.Message}");
        }
        catch (RedisTimeoutException ex)
        {
            context.Logger?.LogError(ex, "redis 操作超时（键 {Key}）。", Key);
            return context.ErrorResult("RedisError", $"Redis operation timed out: {ex.Message}");
        }
        catch (Exception ex)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.UnexpectedError, $"Unexpected error in redis node: {ex.Message}");
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
    private NodeExecutionResult ExecuteGet(IDatabase db, NodeExecutionContext context)
    {
        var value = db.StringGet(Key);
        var exists = !value.IsNull;
        return context.Ok(new JsonObject
        {
            ["key"] = Key,
            ["value"] = exists ? value.ToString() : null,
            ["exists"] = exists
        });
    }

    /// <summary>
    /// Set：写入字符串值（可选 TTL）；返回 <c>success=true</c>。
    /// </summary>
    private NodeExecutionResult ExecuteSet(IDatabase db, NodeExecutionContext context)
    {
        var expiry = Ttl.HasValue && Ttl.Value > 0 ? TimeSpan.FromSeconds(Ttl.Value) : (TimeSpan?)null;
        db.StringSet(Key, Value ?? string.Empty, expiry, When.Always, CommandFlags.None);
        return context.Ok(new JsonObject
        {
            ["key"] = Key,
            ["success"] = true
        });
    }

    /// <summary>
    /// Del：删除键；返回 <c>deleted</c> 布尔值。
    /// </summary>
    private NodeExecutionResult ExecuteDel(IDatabase db, NodeExecutionContext context)
    {
        var deleted = db.KeyDelete(Key);
        return context.Ok(new JsonObject
        {
            ["key"] = Key,
            ["deleted"] = deleted
        });
    }

    /// <summary>
    /// Expire：设置过期时间；返回 <c>success</c> 布尔值。
    /// </summary>
    private NodeExecutionResult ExecuteExpire(IDatabase db, NodeExecutionContext context)
    {
        var expiry = Ttl.HasValue && Ttl.Value > 0 ? TimeSpan.FromSeconds(Ttl.Value) : (TimeSpan?)null;
        var success = db.KeyExpire(Key, expiry, ExpireWhen.Always, CommandFlags.None);
        return context.Ok(new JsonObject
        {
            ["key"] = Key,
            ["success"] = success
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
