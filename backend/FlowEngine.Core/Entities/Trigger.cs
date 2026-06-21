using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FlowEngine.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 触发器。
/// </summary>
[Table("triggers")]
[Comment("触发器")]
public class Trigger : Entity
{
    /// <summary>
    /// 关联工作流定义 ID。
    /// </summary>
    [Required]
    [Column("workflow_definition_id")]
    [Comment("关联工作流定义 ID")]
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>
    /// 工作流版本号。
    /// </summary>
    [Column("workflow_version")]
    [Comment("工作流版本号")]
    public int WorkflowVersion { get; set; }

    /// <summary>
    /// 触发器类型。
    /// </summary>
    [Required]
    [Column("type")]
    [Comment("触发器类型")]
    public TriggerType Type { get; set; }

    /// <summary>
    /// 触发器名称。
    /// </summary>
    [Required]
    [MaxLength(256)]
    [Column("name")]
    [Comment("触发器名称")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 是否激活。
    /// </summary>
    [Column("is_active")]
    [Comment("是否激活")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 触发器配置 JSON。
    /// </summary>
    [Column("settings")]
    [Comment("触发器配置 JSON")]
    public string SettingsJson { get; set; } = "{}";

    /// <summary>
    /// 最后触发时间。
    /// </summary>
    [Column("last_triggered_at")]
    [Comment("最后触发时间")]
    public DateTime? LastTriggeredAt { get; set; }

    /// <summary>
    /// 下次触发时间。
    /// </summary>
    [Column("next_trigger_at")]
    [Comment("下次触发时间")]
    public DateTime? NextTriggerAt { get; set; }
}
