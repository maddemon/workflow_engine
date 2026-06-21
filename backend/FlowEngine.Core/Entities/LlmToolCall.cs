namespace FlowEngine.Core.Entities;

/// <summary>
/// LLM 工具调用。
/// </summary>
public class LlmToolCall
{
    /// <summary>
    /// 工具调用唯一标识。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 工具名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 工具参数（JSON 字符串）。
    /// </summary>
    public string Arguments { get; set; } = string.Empty;
}
