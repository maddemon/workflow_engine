namespace FlowEngine.Application.Dtos;

/// <summary>
/// 工作流导出结果。
/// </summary>
public sealed record WorkflowExportResult
{
    /// <summary>
    /// 工作流名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 工作流版本号。
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// 节点定义列表。
    /// </summary>
    public List<NodeDefinitionDto> Nodes { get; init; } = [];

    /// <summary>
    /// 连接列表。
    /// </summary>
    public List<ConnectionDto> Connections { get; init; } = [];

    /// <summary>
    /// 导出时间。
    /// </summary>
    public DateTime ExportedAt { get; init; }

    /// <summary>
    /// 导出人。
    /// </summary>
    public string ExportedBy { get; init; } = string.Empty;

    /// <summary>
    /// 样式设置。
    /// </summary>
    public Dictionary<string, object?>? StyleSettings { get; init; }
}

/// <summary>
/// 单个工作流的导入结果。
/// </summary>
public sealed record ImportResult
{
    /// <summary>
    /// 是否成功。
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 导入后的工作流 ID。
    /// </summary>
    public Guid? WorkflowId { get; init; }

    /// <summary>
    /// 工作流名称。
    /// </summary>
    public string? WorkflowName { get; init; }

    /// <summary>
    /// 错误详情列表。
    /// </summary>
    public List<ImportError> Errors { get; init; } = [];
}

/// <summary>
/// 批量导入结果。
/// </summary>
public sealed record BatchImportResult
{
    /// <summary>
    /// 成功数量。
    /// </summary>
    public int SuccessCount { get; init; }

    /// <summary>
    /// 失败数量。
    /// </summary>
    public int FailureCount { get; init; }

    /// <summary>
    /// 每个工作流的导入结果。
    /// </summary>
    public List<ImportResult> Results { get; init; } = [];
}

/// <summary>
/// 导入错误详情。
/// </summary>
public sealed record ImportError
{
    /// <summary>
    /// 错误类型：Validation / NodeNotFound / PortNotFound / ConnectionError。
    /// </summary>
    public string ErrorType { get; init; } = string.Empty;

    /// <summary>
    /// 错误消息。
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 相关节点 ID（如适用）。
    /// </summary>
    public string? NodeId { get; init; }

    /// <summary>
    /// 相关连接 ID（如适用）。
    /// </summary>
    public string? ConnectionId { get; init; }
}

/// <summary>
/// 导入工作流请求。
/// </summary>
public sealed record ImportWorkflowRequest
{
    /// <summary>
    /// 要导入的工作流 JSON 内容。
    /// </summary>
    public string Json { get; init; } = string.Empty;

    /// <summary>
    /// 导入到的目标项目 ID（为空时使用导出来源的项目 ID）。
    /// </summary>
    public Guid? ProjectId { get; init; }

    /// <summary>
    /// 导入人。
    /// </summary>
    public string ImportedBy { get; init; } = string.Empty;
}

/// <summary>
/// 批量导入工作流请求。
/// </summary>
public sealed record ImportBatchRequest
{
    /// <summary>
    /// JSON 数组，每个元素是一个 WorkflowExportResult。
    /// </summary>
    public string Json { get; init; } = string.Empty;

    /// <summary>
    /// 导入到的目标项目 ID。
    /// </summary>
    public Guid? ProjectId { get; init; }

    /// <summary>
    /// 导入人。
    /// </summary>
    public string ImportedBy { get; init; } = string.Empty;
}

/// <summary>
/// 批量导出工作流请求。
/// </summary>
public sealed record ExportBatchRequest
{
    /// <summary>
    /// 要导出的工作流 ID 列表。
    /// </summary>
    public List<Guid> Ids { get; init; } = [];
}
