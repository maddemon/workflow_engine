using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;

namespace FlowEngine.Application.Dtos;

/// <summary>
/// AI DSL 装配请求——从 AI 草稿构建完整工作流。
/// </summary>
public sealed record AssembleWorkflowRequest
{
    /// <summary>
    /// 工作流名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 项目 ID（可选）。
    /// </summary>
    public Guid? ProjectId { get; init; }

    /// <summary>
    /// AI 草稿节点列表。
    /// </summary>
    public List<AiDraftNodeDto> Nodes { get; init; } = [];

    /// <summary>
    /// AI 草稿连接列表。
    /// </summary>
    public List<AiDraftConnectionDto> Connections { get; init; } = [];
}

/// <summary>
/// AI 草稿节点（仅含 AI 能生成的最简字段）。
/// </summary>
public sealed record AiDraftNodeDto
{
    /// <summary>
    /// 节点 ID（AI 生成的自然名称，如 "fetch"）。
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 节点类型名。
    /// </summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary>
    /// 参数字典。
    /// </summary>
    public Dictionary<string, object> Parameters { get; init; } = [];
}

/// <summary>
/// AI 草稿连接（极简结构：仅 source/target 节点引用）。
/// </summary>
public sealed record AiDraftConnectionDto
{
    /// <summary>
    /// 源节点 ID。
    /// </summary>
    public string From { get; init; } = string.Empty;

    /// <summary>
    /// 源端口名称（为空时由后端推导为第一个 Output 端口）。
    /// </summary>
    public string? FromPort { get; init; }

    /// <summary>
    /// 目标节点 ID。
    /// </summary>
    public string To { get; init; } = string.Empty;

    /// <summary>
    /// 目标端口名称（为空时由后端推导为第一个 Input 端口）。
    /// </summary>
    public string? ToPort { get; init; }
}

/// <summary>
/// 装配结果。
/// </summary>
public sealed record AssembleWorkflowResult
{
    /// <summary>
    /// 草稿工作流 ID。
    /// </summary>
    public Guid DraftId { get; init; }

    /// <summary>
    /// 完整装配后的工作流 DTO。
    /// </summary>
    public WorkflowDto Workflow { get; init; } = null!;
}

/// <summary>
/// 修改工作流请求。
/// </summary>
public sealed record ModifyWorkflowRequest
{
    /// <summary>
    /// 操作列表。
    /// </summary>
    public List<WorkflowOperation> Operations { get; init; } = [];
}

/// <summary>
/// 工作流修改操作。
/// </summary>
public sealed record WorkflowOperation
{
    /// <summary>
    /// 操作类型："add" | "remove" | "modify" | "connect" | "disconnect" | "move"
    /// </summary>
    public string Op { get; init; } = string.Empty;

    /// <summary>
    /// JSON Pointer 路径，如 "/nodes/fetch/parameters/method"。
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// 操作值（modify 时新值，move 时新坐标等）。
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// 新增节点定义（add 时使用）。
    /// </summary>
    public AiDraftNodeDto? Node { get; init; }

    /// <summary>
    /// 将新节点放置在该节点之后（add 时使用）。
    /// </summary>
    public string? After { get; init; }

    /// <summary>
    /// 源节点 ID（connect / disconnect 时使用）。
    /// </summary>
    public string? From { get; init; }

    /// <summary>
    /// 源端口名称（connect 时使用）。
    /// </summary>
    public string? FromPort { get; init; }

    /// <summary>
    /// 目标节点 ID（connect / disconnect 时使用）。
    /// </summary>
    public string? To { get; init; }

    /// <summary>
    /// 目标端口名称（connect 时使用）。
    /// </summary>
    public string? ToPort { get; init; }
}

/// <summary>
/// 修改结果。
/// </summary>
public sealed record ModifyWorkflowResult
{
    /// <summary>
    /// 草稿工作流 ID。
    /// </summary>
    public Guid DraftId { get; init; }

    /// <summary>
    /// 修改后的工作流 DTO。
    /// </summary>
    public WorkflowDto Workflow { get; init; } = null!;

    /// <summary>
    /// 结构化差异列表。
    /// </summary>
    public List<StructuredDiff> Diff { get; init; } = [];
}

/// <summary>
/// 校验工作流请求。
/// </summary>
public sealed record ValidateWorkflowRequest
{
    /// <summary>
    /// 节点列表（与 WorkflowId 二选一）。
    /// </summary>
    public List<NodeDefinitionDto>? Nodes { get; init; }

    /// <summary>
    /// 连接列表（与 WorkflowId 二选一）。
    /// </summary>
    public List<ConnectionDto>? Connections { get; init; }

    /// <summary>
    /// 已持久化的工作流 ID（与 Nodes/Connections 二选一）。
    /// </summary>
    public Guid? WorkflowId { get; init; }
}

/// <summary>
/// 校验结果。
/// </summary>
public sealed record ValidateWorkflowResult
{
    /// <summary>
    /// 是否通过校验。
    /// </summary>
    public bool Valid { get; init; }

    /// <summary>
    /// 错误列表。
    /// </summary>
    public List<ValidationError> Errors { get; init; } = [];

    /// <summary>
    /// 是否可自动修复。
    /// </summary>
    public bool CanAutoFix { get; init; }

    /// <summary>
    /// 当前重试次数。
    /// </summary>
    public int RetryCount { get; init; }

    /// <summary>
    /// 最大重试次数。
    /// </summary>
    public int MaxRetries { get; init; } = 3;
}

/// <summary>
/// 校验错误。
/// </summary>
public sealed record ValidationError
{
    /// <summary>
    /// 出错的节点 ID。
    /// </summary>
    public string? NodeId { get; init; }

    /// <summary>
    /// 出错字段。
    /// </summary>
    public string? Field { get; init; }

    /// <summary>
    /// 错误类型："MissingRequired" | "InvalidType" | "InvalidExpression" | "TopologyError"
    /// </summary>
    public string ErrorType { get; init; } = string.Empty;

    /// <summary>
    /// 错误描述。
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 期望的 JSON Schema（可选）。
    /// </summary>
    public JsonNode? Schema { get; init; }

    /// <summary>
    /// 自动修复建议（可选）。
    /// </summary>
    public string? SuggestedFix { get; init; }
}

/// <summary>
/// 执行反馈请求。
/// </summary>
public sealed record ExecutionFeedbackRequest
{
    /// <summary>
    /// 执行 ID。
    /// </summary>
    public Guid ExecutionId { get; init; }
}

/// <summary>
/// 执行反馈结果。
/// </summary>
public sealed record ExecutionFeedbackResult
{
    /// <summary>
    /// 执行是否成功。
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 反馈节点列表。
    /// </summary>
    public List<ExecutionFeedbackNode> Nodes { get; init; } = [];

    /// <summary>
    /// 可自动修复标记。
    /// </summary>
    public bool CanAutoFix { get; init; }
}

/// <summary>
/// 执行反馈节点信息。
/// </summary>
public sealed record ExecutionFeedbackNode
{
    /// <summary>
    /// 节点 ID。
    /// </summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>
    /// 节点名称。
    /// </summary>
    public string NodeName { get; init; } = string.Empty;

    /// <summary>
    /// 节点类型。
    /// </summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary>
    /// 执行状态。
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// 错误类型。
    /// </summary>
    public string? ErrorType { get; init; }

    /// <summary>
    /// 错误消息。
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 建议修复方案。
    /// </summary>
    public string? SuggestedFix { get; init; }

    /// <summary>
    /// 执行上下文，供 AI 自纠参考（含节点原始参数与输入数据批次）。
    /// </summary>
    public object? ExecutionContext { get; init; }
}
