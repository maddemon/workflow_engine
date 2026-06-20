using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FlowEngine.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Core.Identity;

/// <summary>
/// 用户角色实体。
/// </summary>
[Table("user_roles")]
[Comment("用户角色")]
[Index(nameof(UserId), nameof(Role), IsUnique = true)]
public class UserRole : Entity
{
    /// <summary>
    /// 用户 ID。
    /// </summary>
    [Required]
    [Comment("用户 ID")]
    public Guid UserId { get; set; }

    /// <summary>
    /// 角色名称。
    /// </summary>
    [Required]
    [MaxLength(64)]
    [Comment("角色名称")]
    public string Role { get; set; } = string.Empty;
}
