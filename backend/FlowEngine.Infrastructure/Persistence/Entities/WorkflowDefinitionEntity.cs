using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FlowEngine.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Infrastructure.Persistence.Entities;

/// <summary>
/// 工作流定义数据库实体。
/// </summary>
[Table("workflow_definitions")]
[Comment("工作流定义")]
[Index(nameof(Name))]
[PrimaryKey(nameof(Id), nameof(Version))]
public sealed class WorkflowDefinitionEntity : Entity
{
    /// <summary>
    /// 项目 ID。
    /// </summary>
    [Comment("项目 ID")]
    public Guid? ProjectId { get; set; }

    /// <summary>
    /// 工作流名称。
    /// </summary>
    [Required]
    [MaxLength(256)]
    [Comment("工作流名称")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 版本号。
    /// </summary>
    [Comment("版本号")]
    public int Version { get; set; }

    /// <summary>
    /// 创建人。
    /// </summary>
    [Required]
    [MaxLength(256)]
    [Comment("创建人")]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// 是否激活。
    /// </summary>
    [Comment("是否激活")]
    public bool IsActive { get; set; }

    /// <summary>
    /// 样式设置 JSON。
    /// </summary>
    [Comment("样式设置")]
    public string? StyleSettingsJson { get; set; }

    /// <summary>
    /// 节点实例列表 JSON。
    /// </summary>
    [Column("nodes")]
    [Comment("节点实例列表 JSON")]
    public string NodesJson { get; set; } = "[]";

    /// <summary>
    /// 连接列表 JSON。
    /// </summary>
    [Column("connections")]
    [Comment("连接列表 JSON")]
    public string ConnectionsJson { get; set; } = "[]";
}
