namespace FlowEngine.Application.Dtos;

/// <summary>
/// 项目响应 DTO。
/// </summary>
public sealed record ProjectDto
{
    /// <summary>
    /// 项目 ID。
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// 项目名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 项目描述。
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 创建人用户 ID。
    /// </summary>
    public Guid CreatedBy { get; init; }

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// 更新时间。
    /// </summary>
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// 创建项目请求。
/// </summary>
public sealed record CreateProjectDto
{
    /// <summary>
    /// 项目名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 项目描述。
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// 更新项目请求。
/// </summary>
public sealed record UpdateProjectDto
{
    /// <summary>
    /// 项目名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 项目描述。
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// 项目成员响应 DTO。
/// </summary>
public sealed record ProjectMemberDto
{
    /// <summary>
    /// 成员 ID。
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// 项目 ID。
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// 用户 ID。
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// 成员角色。
    /// </summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// 添加项目成员请求。
/// </summary>
public sealed record AddProjectMemberDto
{
    /// <summary>
    /// 用户 ID。
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// 成员角色（Admin/Editor/Viewer）。
    /// </summary>
    public string Role { get; init; } = string.Empty;
}

/// <summary>
/// 更新项目成员角色请求。
/// </summary>
public sealed record UpdateProjectMemberDto
{
    /// <summary>
    /// 新的成员角色。
    /// </summary>
    public string Role { get; init; } = string.Empty;
}
