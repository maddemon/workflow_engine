using FlowEngine.Core.Dtos;

namespace FlowEngine.Core.Agent;

/// <summary>
/// InlineResolver 执行结果。
/// </summary>
public sealed class InlineResolverResult
{
    /// <summary>
    /// 最终 LLM 响应内容。
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 实际执行的迭代记录列表。
    /// </summary>
    public List<AgentIterationDto> Iterations { get; set; } = new();

    /// <summary>
    /// 停止原因。
    /// </summary>
    public InlineResolverStopReason StoppedReason { get; set; }
}

/// <summary>
/// InlineResolver 停止原因枚举。
/// </summary>
public enum InlineResolverStopReason
{
    /// <summary>
    /// 正常完成（LLM 未返回工具调用）。
    /// </summary>
    Completed,

    /// <summary>
    /// 达到最大迭代次数。
    /// </summary>
    MaxIterationsReached,

    /// <summary>
    /// 被取消。
    /// </summary>
    Cancelled
}
