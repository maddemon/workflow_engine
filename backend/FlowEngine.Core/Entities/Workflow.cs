using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 工作流定义。
/// </summary>
[Table("workflows", Schema = "flow")]
[Comment("工作流定义")]
public class Workflow : Entity
{
    /// <summary>
    /// 项目 ID。
    /// </summary>
    [Column("project_id")]
    [Comment("项目 ID")]
    public Guid? ProjectId { get; set; }

    /// <summary>
    /// 工作流名称。
    /// </summary>
    [Required]
    [MaxLength(256)]
    [Column("name")]
    [Comment("工作流名称")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 版本号。
    /// </summary>
    [Column("version")]
    [Comment("版本号")]
    public int Version { get; set; }

    /// <summary>
    /// 创建人。
    /// </summary>
    [Required]
    [MaxLength(256)]
    [Column("created_by")]
    [Comment("创建人")]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// 节点实例列表。
    /// </summary>
    [Column("nodes")]
    [Comment("节点实例列表")]
    public List<NodeInstance> Nodes { get; set; } = [];

    /// <summary>
    /// 连接列表。
    /// </summary>
    [Column("connections")]
    [Comment("连接列表")]
    public List<Connection> Connections { get; set; } = [];

    /// <summary>
    /// 是否激活。
    /// </summary>
    [Column("is_active")]
    [Comment("是否激活")]
    public bool IsActive { get; set; }

    /// <summary>
    /// 样式设置，如布局方向等。
    /// </summary>
    public WorkflowStyleSettings? StyleSettings { get; set; }
}
