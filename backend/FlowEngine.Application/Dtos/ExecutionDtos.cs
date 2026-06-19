namespace FlowEngine.Application.Dtos;

/// <summary>
/// 启动执行请求。
/// </summary>
public sealed record StartExecutionDto
{
    /// <summary>
    /// 工作流定义 ID。
    /// </summary>
    public Guid WorkflowId { get; init; }
}

/// <summary>
/// 执行详情响应。
/// </summary>
public sealed record ExecutionDto
{
    /// <summary>
    /// 执行 ID。
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// 工作流定义 ID。
    /// </summary>
    public Guid WorkflowDefinitionId { get; init; }

    /// <summary>
    /// 执行状态。
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// 开始时间。
    /// </summary>
    public DateTime StartedAt { get; init; }

    /// <summary>
    /// 完成时间。
    /// </summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>
    /// 节点执行记录列表。
    /// </summary>
    public List<NodeExecutionRecordDto> NodeRecords { get; init; } = [];
}

/// <summary>
/// 节点执行记录响应。
/// </summary>
public sealed record NodeExecutionRecordDto
{
    /// <summary>
    /// 主键。
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// 节点定义 ID。
    /// </summary>
    public Guid NodeDefinitionId { get; init; }

    /// <summary>
    /// 运行索引。
    /// </summary>
    public int RunIndex { get; init; }

    /// <summary>
    /// 执行状态。
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// 开始时间。
    /// </summary>
    public DateTime StartedAt { get; init; }

    /// <summary>
    /// 完成时间。
    /// </summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>
    /// 输入数据。
    /// </summary>
    public Dictionary<string, object>? Inputs { get; init; }

    /// <summary>
    /// 节点执行结果。
    /// </summary>
    public object? Output { get; init; }

    /// <summary>
    /// 原始参数。
    /// </summary>
    public Dictionary<string, object>? RawParameters { get; init; }

    /// <summary>
    /// 解析后的参数。
    /// </summary>
    public Dictionary<string, object>? ResolvedParameters { get; init; }
}

/// <summary>
/// 执行摘要响应。
/// </summary>
public sealed record ExecutionSummaryDto
{
    /// <summary>
    /// 执行 ID。
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// 工作流定义 ID。
    /// </summary>
    public Guid WorkflowDefinitionId { get; init; }

    /// <summary>
    /// 执行状态。
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// 开始时间。
    /// </summary>
    public DateTime StartedAt { get; init; }

    /// <summary>
    /// 完成时间。
    /// </summary>
    public DateTime? CompletedAt { get; init; }
}
