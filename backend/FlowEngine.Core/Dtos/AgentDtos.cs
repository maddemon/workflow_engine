namespace FlowEngine.Core.Dtos;

/// <summary>
/// Agent 执行信息 DTO，与前端 AgentExecutionInfo 类型对齐（GAP-25）。
/// </summary>
public sealed record AgentExecutionInfoDto
{
    /// <summary>
    /// LLM 模型名称。
    /// </summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// 迭代次数。
    /// </summary>
    public int IterationCount { get; init; }

    /// <summary>
    /// 执行状态（ExecutionStatus 枚举的字符串形式）。
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// 开始时间（ISO 8601 字符串）。
    /// </summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>
    /// 完成时间（ISO 8601 字符串）。
    /// </summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>
    /// 错误信息。
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Token 用量统计。
    /// </summary>
    public TokenUsageDto? TokenUsage { get; init; }
}

/// <summary>
/// Token 用量统计 DTO，与前端 TokenUsage 类型对齐（GAP-25）。
/// </summary>
public sealed record TokenUsageDto
{
    /// <summary>
    /// 提示词 Token 数。
    /// </summary>
    public int PromptTokens { get; init; }

    /// <summary>
    /// 补全 Token 数。
    /// </summary>
    public int CompletionTokens { get; init; }

    /// <summary>
    /// 总 Token 数。
    /// </summary>
    public int TotalTokens { get; init; }
}

/// <summary>
/// 工具调用记录 DTO，与前端 ToolCallRecord 类型对齐（GAP-25）。
/// </summary>
public sealed record ToolCallRecordDto
{
    /// <summary>
    /// 工具调用唯一标识。
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 工具名称。
    /// </summary>
    public string ToolName { get; init; } = string.Empty;

    /// <summary>
    /// 工具输入（任意 JSON 值）。
    /// </summary>
    public object? Input { get; init; }

    /// <summary>
    /// 工具输出（任意 JSON 值）。
    /// </summary>
    public object? Output { get; init; }

    /// <summary>
    /// 执行状态（ExecutionStatus 枚举的字符串形式）。
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// 执行时长（毫秒）。
    /// </summary>
    public double? Duration { get; init; }

    /// <summary>
    /// 错误信息。
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// LLM 流式输出块 DTO，与前端 LLMChunk 类型对齐（GAP-25）。
/// </summary>
public sealed record LlmChunkDto
{
    /// <summary>
    /// 文本内容。
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// 角色（assistant / system / user）。
    /// </summary>
    public string Role { get; init; } = "assistant";

    /// <summary>
    /// 时间戳（ISO 8601 字符串）。
    /// </summary>
    public string Timestamp { get; init; } = string.Empty;
}

/// <summary>
/// Agent 迭代记录 DTO，与前端 AgentIteration 类型对齐（GAP-25）。
/// </summary>
public sealed record AgentIterationDto
{
    /// <summary>
    /// 迭代索引（从 0 开始）。
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// LLM 输出块列表。
    /// </summary>
    public List<LlmChunkDto> LlmChunks { get; init; } = [];

    /// <summary>
    /// 工具调用列表。
    /// </summary>
    public List<ToolCallRecordDto> ToolCalls { get; init; } = [];

    /// <summary>
    /// 开始时间（ISO 8601 字符串）。
    /// </summary>
    public string? StartedAt { get; init; }

    /// <summary>
    /// 完成时间（ISO 8601 字符串）。
    /// </summary>
    public string? CompletedAt { get; init; }
}

/// <summary>
/// 子 Agent 记录 DTO，与前端 SubRecord 类型对齐（GAP-25）。
/// </summary>
public sealed record SubRecordDto
{
    /// <summary>
    /// 父记录 ID。
    /// </summary>
    public string ParentId { get; init; } = string.Empty;

    /// <summary>
    /// 子 Agent 名称。
    /// </summary>
    public string AgentName { get; init; } = string.Empty;

    /// <summary>
    /// 子 Agent 的迭代记录列表。
    /// </summary>
    public List<AgentIterationDto> Records { get; init; } = [];

    /// <summary>
    /// 执行状态（ExecutionStatus 枚举的字符串形式）。
    /// </summary>
    public string Status { get; init; } = string.Empty;
}

/// <summary>
/// Agent 执行结果 DTO，与前端 AgentExecutionData 类型对齐（GAP-25）。
/// 作为 AgentNode 输出和前端渲染的统一数据契约。
/// </summary>
public sealed record AgentExecutionResultDto
{
    /// <summary>
    /// Agent 执行信息。
    /// </summary>
    public AgentExecutionInfoDto AgentInfo { get; init; } = new();

    /// <summary>
    /// 迭代记录列表。
    /// </summary>
    public List<AgentIterationDto> Iterations { get; init; } = [];

    /// <summary>
    /// 子 Agent 记录列表。
    /// </summary>
    public List<SubRecordDto> SubRecords { get; init; } = [];

    /// <summary>
    /// 系统提示词。
    /// </summary>
    public string? SystemPrompt { get; init; }
}
