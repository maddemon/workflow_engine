using System.Text.Json.Nodes;

namespace FlowEngine.Core.Ai;

/// <summary>
/// AI 示例，描述节点的输入输出样例。
/// </summary>
public sealed class AiExample
{
    /// <summary>
    /// 示例描述。
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 示例输入。
    /// </summary>
    public JsonNode? Input { get; set; }

    /// <summary>
    /// 示例输出。
    /// </summary>
    public JsonNode? Output { get; set; }
}
