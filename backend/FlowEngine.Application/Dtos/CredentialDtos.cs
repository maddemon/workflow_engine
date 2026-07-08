using System.ComponentModel.DataAnnotations;

namespace FlowEngine.Application.Dtos;

/// <summary>
/// 创建凭据请求。
/// </summary>
public sealed record CreateCredentialDto
{
    /// <summary>
    /// 项目 ID。
    /// </summary>
    public Guid? ProjectId { get; init; }

    /// <summary>
    /// 凭据名称。
    /// </summary>
    [Required]
    [StringLength(256)]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 凭据类型。
    /// </summary>
    [Required]
    [StringLength(128)]
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// 明文字段映射（将被加密存储）。
    /// </summary>
    [Required]
    [MinLength(1)]
    public Dictionary<string, string> Fields { get; init; } = [];
}

/// <summary>
/// 更新凭据请求。
/// </summary>
public sealed record UpdateCredentialDto
{
    /// <summary>
    /// 凭据名称。
    /// </summary>
    [Required]
    [StringLength(256)]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 明文字段映射（将被加密存储）。
    /// </summary>
    [Required]
    [MinLength(1)]
    public Dictionary<string, string> Fields { get; init; } = [];
}

/// <summary>
/// 凭据响应。
/// </summary>
public sealed record CredentialDto
{
    /// <summary>
    /// 凭据 ID。
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// 项目 ID。
    /// </summary>
    public Guid? ProjectId { get; init; }

    /// <summary>
    /// 凭据名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 凭据类型。
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// 凭据明文字段（已解密），查看者角色下将被脱敏为 ***。
    /// </summary>
    public Dictionary<string, string> Fields { get; init; } = [];

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// 更新时间。
    /// </summary>
    public DateTime? UpdatedAt { get; init; }
}
