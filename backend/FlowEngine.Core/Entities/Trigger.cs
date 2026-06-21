using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FlowEngine.Core.Attributes;
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
    [Comment("触发器配置")]
    [JsonColumn]
    public TriggerSettings Settings { get; set; } = new();

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

[NotMapped]
public sealed class TriggerSettings
{
    /// <summary>
    /// Cron 表达式（Schedule 类型）。
    /// </summary>
    public string? CronExpression { get; set; }

    /// <summary>
    /// 时区（Schedule 类型）。
    /// </summary>
    public string? TimeZone { get; set; }

    /// <summary>
    /// 开始时间（Schedule 类型）。
    /// </summary>
    public DateTime? StartAt { get; set; }

    /// <summary>
    /// 结束时间（Schedule 类型）。
    /// </summary>
    public DateTime? EndAt { get; set; }

    /// <summary>
    /// Webhook 路径（Webhook 类型）。
    /// </summary>
    public string? WebhookPath { get; set; }

    /// <summary>
    /// 签名密钥（Webhook 类型）。
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>
    /// IP 白名单（Webhook 类型）。
    /// </summary>
    public List<string>? AllowedIps { get; set; }

    /// <summary>
    /// 来源域白名单（Webhook 类型）。
    /// </summary>
    public List<string>? AllowedOrigins { get; set; }

    /// <summary>
    /// 是否同步响应（Webhook 类型）。
    /// </summary>
    public bool IsSync { get; set; }

    /// <summary>
    /// 同步响应最大等待时间（秒）（Webhook 类型）。
    /// </summary>
    public int MaxWaitSeconds { get; set; } = 30;
}
