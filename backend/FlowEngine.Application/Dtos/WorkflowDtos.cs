using FlowEngine.Core.Entities;

namespace FlowEngine.Application.Dtos;

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
    public List<NodeInstance> Nodes { get; init; } = [];

    /// <summary>
    /// 连接列表。
    /// </summary>
    public List<Connection> Connections { get; init; } = [];
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
    public List<NodeInstance> Nodes { get; init; } = [];

    /// <summary>
    /// 连接列表。
    /// </summary>
    public List<Connection> Connections { get; init; } = [];
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
    public DateTime UpdatedAt { get; init; }

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
    public List<NodeInstance> Nodes { get; init; } = [];

    /// <summary>
    /// 连接列表。
    /// </summary>
    public List<Connection> Connections { get; init; } = [];
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
