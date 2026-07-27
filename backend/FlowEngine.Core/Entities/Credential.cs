using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FlowEngine.Core.Attributes;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 凭据定义。
/// </summary>
[Table("credentials", Schema = "flow")]
[Comment("凭据定义")]
public class Credential : Entity
{
    /// <summary>
    /// 项目 ID。
    /// </summary>
    [Column("project_id")]
    [Comment("项目 ID")]
    public Guid? ProjectId { get; set; }

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
    [MaxLength(64)]
    [Column("type")]
    [Comment("凭据类型")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 加密字段数据映射。
    /// </summary>
    [Column("data")]
    [Comment("加密字段数据映射")]
    [JsonColumn]
    public Dictionary<string, EncryptedField> Data { get; set; } = [];

    /// <summary>
    /// 密钥版本。
    /// </summary>
    [Required]
    [MaxLength(64)]
    [Column("key_version")]
    [Comment("密钥版本")]
    public string KeyVersion { get; set; } = string.Empty;

    /// <summary>
    /// 乐观并发令牌（行版本）。每次新增或变更由 <c>FlowEngineDbContext</c> 在保存前自增，
    /// 用于跨 DbContext 的更新丢失检测（lost update）。类型为 <see cref="long"/> 而非
    /// <see cref="byte"/>[]，因为 SQLite/PostgreSQL/MySQL 不会自动递增 rowversion，
    /// 由应用层统一维护以保证多提供程序行为一致。
    /// </summary>
    [ConcurrencyCheck]
    [Column("row_version")]
    [Comment("乐观并发行版本")]
    public long RowVersion { get; set; }
}
