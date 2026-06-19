using FlowEngine.Core.Entities;

namespace FlowEngine.Runtime.Expressions;

/// <summary>
/// 表达式求值所需的元数据。
/// </summary>
public sealed class ExpressionMetadata
{
    /// <summary>
    /// 当前工作流定义。
    /// </summary>
    public Workflow? Workflow { get; init; }

    /// <summary>
    /// 当前执行 ID。
    /// </summary>
    public Guid ExecutionId { get; init; }

    /// <summary>
    /// 当前运行索引。
    /// </summary>
    public int RunIndex { get; init; }

    /// <summary>
    /// 当前 UTC 时间。
    /// </summary>
    public DateTime Now => DateTime.UtcNow;
}
