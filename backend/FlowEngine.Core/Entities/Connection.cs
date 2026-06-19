using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 节点连接。
/// </summary>
[Table("connections", Schema = "flow")]
[Comment("节点连接")]
public class Connection : Entity
{
    /// <summary>
    /// 源节点实例 ID。
    /// </summary>
    [Column("source_node_definition_id")]
    [Comment("源节点实例 ID")]
    public Guid SourceNodeId { get; set; }

    /// <summary>
    /// 源端口名称。
    /// </summary>
    [Required]
    [MaxLength(128)]
    [Column("source_port_name")]
    [Comment("源端口名称")]
    public string SourcePortName { get; set; } = string.Empty;

    /// <summary>
    /// 目标节点实例 ID。
    /// </summary>
    [Column("target_node_definition_id")]
    [Comment("目标节点实例 ID")]
    public Guid TargetNodeId { get; set; }

    /// <summary>
    /// 目标端口名称。
    /// </summary>
    [Required]
    [MaxLength(128)]
    [Column("target_port_name")]
    [Comment("目标端口名称")]
    public string TargetPortName { get; set; } = string.Empty;

    /// <summary>
    /// 连接条件表达式。
    /// </summary>
    [MaxLength(1024)]
    [Column("condition")]
    [Comment("连接条件表达式")]
    public string? Condition { get; set; }
}
