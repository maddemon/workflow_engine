using System.ComponentModel.DataAnnotations;

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
    [Required]
    [StringLength(256)]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 项目描述。
    /// </summary>
    [StringLength(2000)]
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
    [Required]
    [StringLength(256)]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 项目描述。
    /// </summary>
    [StringLength(2000)]
    public string? Description { get; init; }
}


