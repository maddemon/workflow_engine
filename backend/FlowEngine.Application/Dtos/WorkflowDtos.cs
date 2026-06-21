using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Application.Dtos;

/// <summary>
/// API 接收的节点定义（不含 Entity 基类，接受字符串 ID）。
/// </summary>
public sealed record NodeDefinitionDto
{
    /// <summary>
    /// 节点 ID（前端生成的字符串标识）。
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 节点类型名。
    /// </summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary>
    /// 节点名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 参数映射。
    /// </summary>
    public Dictionary<string, object> Parameters { get; init; } = [];

    /// <summary>
    /// 端口实例列表。
    /// </summary>
    public List<PortInstance> Ports { get; init; } = [];

    /// <summary>
    /// X 坐标。
    /// </summary>
    public int PositionX { get; init; }

    /// <summary>
    /// Y 坐标。
    /// </summary>
    public int PositionY { get; init; }

    /// <summary>
    /// 是否为入口节点。
    /// </summary>
    public bool IsEntry { get; init; }

    /// <summary>
    /// 重试策略。
    /// </summary>
    public RetryPolicy? RetryPolicy { get; init; }

    /// <summary>
    /// 错误处理策略。
    /// </summary>
    public ErrorStrategy ErrorStrategy { get; init; }

    /// <summary>
    /// 超时时间。
    /// </summary>
    public TimeSpan? Timeout { get; init; }
}

/// <summary>
/// API 接收的连接（不含 Entity 基类，接受字符串 ID）。
/// </summary>
public sealed record ConnectionDto
{
    /// <summary>
    /// 连接 ID（前端生成的字符串标识）。
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 源节点 ID。
    /// </summary>
    public string SourceNodeId { get; init; } = string.Empty;

    /// <summary>
    /// 源端口名称。
    /// </summary>
    public string SourcePortName { get; init; } = string.Empty;

    /// <summary>
    /// 目标节点 ID。
    /// </summary>
    public string TargetNodeId { get; init; } = string.Empty;

    /// <summary>
    /// 目标端口名称。
    /// </summary>
    public string TargetPortName { get; init; } = string.Empty;

    /// <summary>
    /// 连接条件表达式。
    /// </summary>
    public string? Condition { get; init; }
}

/// <summary>
/// 创建工作流请求。
/// </summary>
public sealed record CreateWorkflowDto
{
    /// <summary>
    /// 项目 ID。
    /// </summary>
    public Guid? ProjectId { get; init; }

    /// <summary>
    /// 工作流名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 创建人。
    /// </summary>
    public string CreatedBy { get; init; } = string.Empty;

    /// <summary>
    /// 样式设置。
    /// </summary>
    public Dictionary<string, object?>? StyleSettings { get; init; }

    /// <summary>
    /// 节点实例列表。
    /// </summary>
    public List<NodeDefinitionDto> Nodes { get; init; } = [];

    /// <summary>
    /// 连接列表。
    /// </summary>
    public List<ConnectionDto> Connections { get; init; } = [];
}

/// <summary>
/// 更新工作流请求。
/// </summary>
public sealed record UpdateWorkflowDto
{
    /// <summary>
    /// 工作流名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 是否激活。
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// 样式设置。
    /// </summary>
    public WorkflowStyleSettings? StyleSettings { get; init; }

    /// <summary>
    /// 节点实例列表。
    /// </summary>
    public List<NodeDefinitionDto> Nodes { get; init; } = [];

    /// <summary>
    /// 连接列表。
    /// </summary>
    public List<ConnectionDto> Connections { get; init; } = [];
}

/// <summary>
/// 工作流响应。
/// </summary>
public sealed record WorkflowDto
{
    /// <summary>
    /// 工作流 ID。
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// 项目 ID。
    /// </summary>
    public Guid? ProjectId { get; init; }

    /// <summary>
    /// 工作流名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 版本号。
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// 创建人。
    /// </summary>
    public string CreatedBy { get; init; } = string.Empty;

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// 更新时间。
    /// </summary>
    public DateTime? UpdatedAt { get; init; }

    /// <summary>
    /// 是否激活。
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// 样式设置。
    /// </summary>
    public WorkflowStyleSettings? StyleSettings { get; init; }

    /// <summary>
    /// 节点实例列表。
    /// </summary>
    public List<NodeDefinitionDto> Nodes { get; init; } = [];

    /// <summary>
    /// 连接列表。
    /// </summary>
    public List<ConnectionDto> Connections { get; init; } = [];
}

/// <summary>
/// 工作流列表摘要。
/// </summary>
public sealed record WorkflowSummaryDto
{
    /// <summary>
    /// 工作流 ID。
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// 工作流名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 当前版本号。
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// 是否激活。
    /// </summary>
    public bool IsActive { get; init; }
}

/// <summary>
/// 分页结果。
/// </summary>
public sealed record PagedResult<T>
{
    /// <summary>
    /// 当前页数据。
    /// </summary>
    public IReadOnlyCollection<T> Items { get; init; } = [];

    /// <summary>
    /// 总记录数。
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// 当前页码（从 1 开始）。
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// 每页大小。
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// 总页数。
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / Math.Max(1, PageSize));
}
