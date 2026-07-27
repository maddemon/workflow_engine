using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 项目实体，用于资源分类。
/// </summary>
/// <remarks>
/// 项目不是租户隔离边界，仅作为工作流、凭据、触发器、执行记录、文件等资源的分类维度。
/// </remarks>
[Table("projects", Schema = "flow")]
[Comment("项目")]
public class Project : Entity
{
    /// <summary>
    /// 项目名称。
    /// </summary>
    [Required]
    [MaxLength(256)]
    [Column("name")]
    [Comment("项目名称")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 项目描述。
    /// </summary>
    [MaxLength(1024)]
    [Column("description")]
    [Comment("项目描述")]
    public string? Description { get; set; }

    /// <summary>
    /// 创建人用户 ID。
    /// </summary>
    [Required]
    [Column("created_by")]
    [Comment("创建人")]
    public string CreatedBy { get; set; } = string.Empty;

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
