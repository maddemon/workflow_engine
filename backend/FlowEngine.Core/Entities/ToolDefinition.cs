namespace FlowEngine.Core.Entities;

/// <summary>
/// 工具定义。
/// </summary>
public class ToolDefinition
{
    /// <summary>
    /// 工具名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 工具描述。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 参数 JSON Schema。
    /// </summary>
    public object? ParametersSchema { get; set; }

    /// <summary>
    /// 目标节点定义 ID（引用 NodeDefinition.Id 的字符串标识）。
    /// </summary>
    public string TargetNodeDefinitionId { get; set; } = string.Empty;
}
