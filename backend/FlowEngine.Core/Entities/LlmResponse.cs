namespace FlowEngine.Core.Entities;

/// <summary>
/// LLM 响应。
/// </summary>
public class LlmResponse
{
    /// <summary>
    /// 响应文本内容。
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// 工具调用列表。
    /// </summary>
    public IReadOnlyList<LlmToolCall>? ToolCalls { get; set; }

    /// <summary>
    /// 是否包含工具调用。
    /// </summary>
    public bool HasToolCalls => ToolCalls is { Count: > 0 };

    /// <summary>
    /// 结束原因（如 <c>Stop</c>、<c>Length</c>、<c>ContentFilter</c>），由 LLM 提供方返回。
    /// 用于识别截断或内容过滤等非正常结束，避免静默丢失内容。
    /// </summary>
    public string? FinishReason { get; set; }
}
