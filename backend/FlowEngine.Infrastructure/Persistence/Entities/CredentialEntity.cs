using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FlowEngine.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Infrastructure.Persistence.Entities;

/// <summary>
/// 凭据数据库实体。
/// </summary>
[Table("credentials")]
[Comment("凭据定义")]
[Index(nameof(Name))]
[Index(nameof(Type))]
public sealed class CredentialEntity : Entity
{
    /// <summary>
    /// 凭据名称。
    /// </summary>
    [Required]
    [MaxLength(256)]
    [Column("name")]
    [Comment("凭据名称")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 凭据类型。
    /// </summary>
    [Required]
    [MaxLength(128)]
    [Column("type")]
    [Comment("凭据类型")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 加密字段数据 JSON。
    /// </summary>
    [Column("data")]
    [Comment("加密字段数据 JSON")]
    public string DataJson { get; set; } = "{}";

    /// <summary>
    /// 密钥版本。
    /// </summary>
    [Required]
    [MaxLength(128)]
    [Column("key_version")]
    [Comment("密钥版本")]
    public string KeyVersion { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间。
    /// </summary>
    [Column("created_at")]
    [Comment("创建时间")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 更新时间。
    /// </summary>
    [Column("updated_at")]
    [Comment("更新时间")]
    public DateTime? UpdatedAt { get; set; }
}
