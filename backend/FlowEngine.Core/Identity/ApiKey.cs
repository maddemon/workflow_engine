using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FlowEngine.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Core.Identity;

/// <summary>
/// API Key（Personal Access Token）实体。
/// </summary>
[Table("api_keys", Schema = "flow")]
[Comment("API Key")]
[Index(nameof(UserId))]
[Index(nameof(KeyHash), IsUnique = true)]
public class ApiKey : Entity
{
    /// <summary>
    /// 所属用户 ID。
    /// </summary>
    [Required]
    [Column("user_id")]
    [Comment("所属用户 ID")]
    public Guid UserId { get; set; }

    /// <summary>
    /// 所属用户。
    /// </summary>
    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    /// <summary>
    /// 令牌名称，供用户识别。
    /// </summary>
    [Required]
    [MaxLength(256)]
    [Column("name")]
    [Comment("令牌名称")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 完整 Key 的哈希值，用于验证。
    /// </summary>
    [Required]
    [MaxLength(256)]
    [Column("key_hash")]
    [Comment("完整 Key 的哈希值")]
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>
    /// Key 前缀，用于列表展示。
    /// </summary>
    [Required]
    [MaxLength(16)]
    [Column("prefix")]
    [Comment("Key 前缀")]
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// 过期时间，null 表示永不过期。
    /// </summary>
    [Column("expires_at")]
    [Comment("过期时间")]
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// 吊销时间，null 表示未吊销。
    /// </summary>
    [Column("revoked_at")]
    [Comment("吊销时间")]
    public DateTime? RevokedAt { get; set; }
}
