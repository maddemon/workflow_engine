namespace FlowEngine.Core.Ai;

/// <summary>
/// AI 节点摘要，用于列表展示。
/// </summary>
public sealed class AiNodeSummary
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
}
