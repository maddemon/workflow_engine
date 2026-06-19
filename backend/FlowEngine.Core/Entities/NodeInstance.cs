using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FlowEngine.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 节点实例。
/// </summary>
[Table("node_instances", Schema = "flow")]
[Comment("节点实例")]
public class NodeInstance : Entity
{
    /// <summary>
    /// 节点类型名。
    /// </summary>
    [Required]
    [MaxLength(128)]
    [Column("type_name")]
    [Comment("节点类型名")]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// 节点名称。
    /// </summary>
    [Required]
    [MaxLength(256)]
    [Column("name")]
    [Comment("节点名称")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 参数映射。
    /// </summary>
    [Column("parameters")]
    [Comment("参数映射")]
    public Dictionary<string, object> Parameters { get; set; } = [];

    /// <summary>
    /// 端口实例列表。
    /// </summary>
    [Column("ports")]
    [Comment("端口实例列表")]
    public List<PortInstance> Ports { get; set; } = [];

    /// <summary>
    /// X 坐标。
    /// </summary>
    [Column("position_x")]
    [Comment("X 坐标")]
    public int PositionX { get; set; }

    /// <summary>
    /// Y 坐标。
    /// </summary>
    [Column("position_y")]
    [Comment("Y 坐标")]
    public int PositionY { get; set; }

    /// <summary>
    /// 是否为入口节点。
    /// </summary>
    [Column("is_entry")]
    [Comment("是否为入口节点")]
    public bool IsEntry { get; set; }

    /// <summary>
    /// 重试策略。
    /// </summary>
    [Column("retry_policy")]
    [Comment("重试策略")]
    public RetryPolicy? RetryPolicy { get; set; }

    /// <summary>
    /// 错误处理策略。
    /// </summary>
    [Column("error_strategy")]
    [Comment("错误处理策略")]
    public ErrorStrategy ErrorStrategy { get; set; }

    /// <summary>
    /// 超时时间。
    /// </summary>
    [Column("timeout")]
    [Comment("超时时间")]
    public TimeSpan? Timeout { get; set; }
}
