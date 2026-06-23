using System.ComponentModel;

namespace FlowEngine.Core.Enums;

/// <summary>
/// 端口类型。
/// </summary>
public enum PortType
{
    /// <summary>
    /// 主数据端口。
    /// </summary>
    [Description("主数据端口")]
    Main,

    /// <summary>
    /// Agent 工具端口。
    /// </summary>
    [Description("Agent 工具端口")]
    AgentTool,

    /// <summary>
    /// LLM 供应端口。
    /// </summary>
    [Description("LLM 供应端口")]
    LLM,

    /// <summary>
    /// 记忆端口。
    /// </summary>
    [Description("记忆端口")]
    Memory
}
