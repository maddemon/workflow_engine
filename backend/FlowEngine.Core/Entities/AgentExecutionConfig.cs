using System.ComponentModel.DataAnnotations;

namespace FlowEngine.Core.Entities;

/// <summary>
/// Agent 执行配置，控制迭代次数、嵌套深度、记忆窗口等行为。
/// </summary>
public class AgentExecutionConfig
{
    /// <summary>
    /// 最大 LLM 迭代次数。
    /// </summary>
    [Range(1, 100)]
    public int MaxIterations { get; set; } = 10;

    /// <summary>
    /// 最大嵌套深度（子 Agent 层数）。
    /// </summary>
    [Range(0, 10)]
    public int MaxNestingDepth { get; set; } = 3;

    /// <summary>
    /// 是否启用对话记忆。
    /// </summary>
    public bool MemoryEnabled { get; set; }

    /// <summary>
    /// 记忆窗口大小（保留最近 N 条消息）。
    /// </summary>
    [Range(1, 1000)]
    public int MemoryWindowSize { get; set; } = 20;
}
