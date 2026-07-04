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
    public Guid CreatedBy { get; set; }
}
