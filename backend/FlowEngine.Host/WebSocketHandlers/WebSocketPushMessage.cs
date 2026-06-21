using System.Text.Json.Serialization;

namespace FlowEngine.Host.WebSocketHandlers;

/// <summary>
/// WebSocket 推送消息。
/// </summary>
public record WebSocketPushMessage
{
    /// <summary>
    /// 消息类型。
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// 执行 ID。
    /// </summary>
    [JsonPropertyName("executionId")]
    public Guid ExecutionId { get; init; }

    /// <summary>
    /// 时间戳。
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 事件序号（用于断线重连补偿）。
    /// </summary>
    [JsonPropertyName("sequence")]
    public long Sequence { get; init; }

    /// <summary>
    /// 负载数据。
    /// </summary>
    [JsonPropertyName("payload")]
    public object? Payload { get; init; }
}

/// <summary>
/// 客户端订阅消息。
/// </summary>
public record WebSocketSubscribeMessage
{
    /// <summary>
    /// 消息类型（固定 "subscribe"）。
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "subscribe";

    /// <summary>
    /// 要订阅的执行 ID。
    /// </summary>
    [JsonPropertyName("executionId")]
    public Guid ExecutionId { get; init; }

    /// <summary>
    /// 断线重连时，上次接收的事件序号（用于补发）。
    /// </summary>
    [JsonPropertyName("lastSequence")]
    public long? LastSequence { get; init; }
}

/// <summary>
/// 客户端心跳消息。
/// </summary>
public record WebSocketPingMessage
{
    /// <summary>
    /// 消息类型（固定 "ping"）。
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "ping";
}
