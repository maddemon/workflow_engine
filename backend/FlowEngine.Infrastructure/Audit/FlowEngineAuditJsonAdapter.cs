using System.Text.Json;
using Audit.Core;

namespace FlowEngine.Infrastructure.Audit;

/// <summary>
/// Audit.NET 的 JSON 适配器。
/// 将 <see cref="FlowEngineAuditEvent"/> 序列化为与历史手搓 NDJSON 审计日志完全一致的字段布局
/// （camelCase，且始终包含全部 8 个字段），从而保证 <see cref="AuditLogReader"/> 及既有 API 消费者读取的
/// 字段顺序与结构不变。其余类型回退到标准 System.Text.Json 序列化。
/// </summary>
public sealed class FlowEngineAuditJsonAdapter : IJsonAdapter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static object Project(FlowEngineAuditEvent e) => new
    {
        id = e.Id,
        eventType = e.EventType,
        timestamp = e.Timestamp,
        actor = e.Actor,
        resourceType = e.ResourceType,
        resourceId = e.ResourceId,
        payload = e.Payload,
        metadata = e.Metadata,
    };

    /// <inheritdoc />
    public string Serialize(object value)
    {
        // 显式投影为与原 SerializeEvent 输出完全一致的匿名结构。
        // 注意：保留 null 字段（与原行为一致，不忽略空值），确保 on-disk 字段集合稳定。
        return JsonSerializer.Serialize(value is FlowEngineAuditEvent e ? Project(e) : value, Options);
    }

    /// <inheritdoc />
    public async Task SerializeAsync(Stream stream, object value, CancellationToken cancellationToken = default)
    {
        if (value is FlowEngineAuditEvent e)
        {
            await JsonSerializer.SerializeAsync(stream, Project(e), Options, cancellationToken).ConfigureAwait(false);
            return;
        }

        await JsonSerializer.SerializeAsync(stream, value, value.GetType(), Options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    /// <inheritdoc />
    public object? Deserialize(string json, Type type) => JsonSerializer.Deserialize(json, type, Options);

    /// <inheritdoc />
    public async Task<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
        => await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public T? ToObject<T>(object value)
    {
        if (value is T typed)
        {
            return typed;
        }

        var json = JsonSerializer.Serialize(value, Options);
        return JsonSerializer.Deserialize<T>(json, Options);
    }
}
