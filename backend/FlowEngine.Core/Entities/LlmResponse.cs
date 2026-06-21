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
}
