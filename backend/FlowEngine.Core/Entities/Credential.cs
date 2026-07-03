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
}
