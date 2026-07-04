using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 项目成员关系。
/// </summary>
/// <remarks>
/// 已废弃：系统按非 SaaS、企业内部私有化部署设计，项目仅用于分类，不再使用项目成员进行数据隔离或权限控制。
/// 保留实体仅用于兼容已有数据库表，新逻辑不应依赖此表。
/// </remarks>
[Obsolete("项目成员仅用于兼容历史数据，不再参与权限隔离。")]
[Table("project_members", Schema = "flow")]
[Comment("项目成员（历史兼容）")]
[Index(nameof(ProjectId), nameof(UserId), IsUnique = true)]
public class ProjectMember : Entity
{
    /// <summary>
    /// 项目 ID。
    /// </summary>
    [Required]
    [Column("project_id")]
    [Comment("项目 ID")]
    public Guid ProjectId { get; set; }

    /// <summary>
    /// 用户 ID。
    /// </summary>
    [Required]
    [Column("user_id")]
    [Comment("用户 ID")]
    public Guid UserId { get; set; }

    /// <summary>
    /// 成员角色（Admin/Editor/Viewer）。
    /// </summary>
    [Required]
    [MaxLength(64)]
    [Column("role")]
    [Comment("成员角色")]
    public string Role { get; set; } = string.Empty;
}
