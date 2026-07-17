namespace FlowEngine.Core.Ai;

/// <summary>
/// AI 端口模式。
/// </summary>
public sealed class AiPortSchema
{
    /// <summary>
    /// 端口名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 端口方向（"Input" / "Output"）。
    /// </summary>
    public string Direction { get; set; } = string.Empty;

    /// <summary>
    /// 端口描述。
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 端口类型（Main / AgentTool / LLM / Memory）。
    /// </summary>
    public string? Type { get; set; }
}
