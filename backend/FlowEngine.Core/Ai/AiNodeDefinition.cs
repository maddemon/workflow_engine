using System.Text.Json.Nodes;

namespace FlowEngine.Core.Ai;

/// <summary>
/// AI 节点定义，包含完整语义信息供 AI 发现和调用。
/// </summary>
public sealed class AiNodeDefinition
{
    /// <summary>
    /// 节点类型唯一标识。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 节点描述。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 节点分类。
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 标签列表。
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// 是否为触发器节点。
    /// </summary>
    public bool IsTrigger { get; set; }

    /// <summary>
    /// 输入 JSON Schema。
    /// </summary>
    public JsonNode? InputSchema { get; set; }

    /// <summary>
    /// 输出 JSON Schema。
    /// </summary>
    public JsonNode? OutputSchema { get; set; }

    /// <summary>
    /// 端口列表。
    /// </summary>
    public List<AiPortSchema> Ports { get; set; } = [];

    /// <summary>
    /// 示例列表。
    /// </summary>
    public List<AiExample> Examples { get; set; } = [];

    /// <summary>表达式语言，固定为 "javascript"。用于在 AI 定义中显式声明，消解 n8n 模板假设。</summary>
    public string ExpressionLanguage { get; set; } = "javascript";
}
