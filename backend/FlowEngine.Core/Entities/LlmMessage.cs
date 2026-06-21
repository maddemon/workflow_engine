namespace FlowEngine.Core.Entities;

/// <summary>
/// LLM 对话消息。
/// </summary>
public class LlmMessage
{
    /// <summary>
    /// 消息角色（system、user、assistant、tool）。
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// 消息内容。
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// 工具调用 ID（仅 tool 角色消息使用）。
    /// </summary>
    public string? ToolCallId { get; set; }

    /// <summary>
    /// 工具调用列表（仅 assistant 角色消息使用）。
    /// </summary>
    public IReadOnlyList<LlmToolCall>? ToolCalls { get; set; }
}
