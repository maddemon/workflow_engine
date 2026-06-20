using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FlowEngine.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Core.Identity;

/// <summary>
/// 用户实体。
/// </summary>
[Table("users")]
[Comment("用户")]
[Index(nameof(Email), IsUnique = true)]
public class User : Entity
{
    /// <summary>
    /// 邮箱地址。
    /// </summary>
    [Required]
    [MaxLength(320)]
    [Comment("邮箱地址")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// 用户名。
    /// </summary>
    [Required]
    [MaxLength(128)]
    [Comment("用户名")]
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// 密码哈希值。
    /// </summary>
    [Required]
    [MaxLength(256)]
    [Comment("密码哈希值")]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称。
    /// </summary>
    [MaxLength(256)]
    [Comment("显示名称")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// 是否激活。
    /// </summary>
    [Comment("是否激活")]
    public bool IsActive { get; set; } = true;
}
