namespace FlowEngine.Core.Entities;

/// <summary>
/// LLM 流式输出增量块。
/// </summary>
public class LlmStreamChunk
{
    /// <summary>
    /// 增量文本内容（token 片段）。
    /// </summary>
    public string? Delta { get; set; }

    /// <summary>
    /// 工具调用列表（仅在最后一条 chunk 中有效，由客户端累积得到）。
    /// </summary>
    public IReadOnlyList<LlmToolCall>? ToolCalls { get; set; }

    /// <summary>
    /// 是否为流的最后一条 chunk。
    /// </summary>
    public bool IsFinal { get; set; }

    /// <summary>
    /// 结束原因（如 <c>Stop</c>、<c>Length</c>、<c>ContentFilter</c>），仅最后一条 chunk 有效。
    /// 用于识别截断或内容过滤等非正常结束。
    /// </summary>
    public string? FinishReason { get; set; }
}
