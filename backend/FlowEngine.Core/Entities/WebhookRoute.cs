using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Core.Entities;

/// <summary>
/// Webhook 路由。
/// </summary>
[Table("webhook_routes")]
[Comment("Webhook 路由")]
public class WebhookRoute : Entity
{
    /// <summary>
    /// Webhook 路径。
    /// </summary>
    [Required]
    [MaxLength(512)]
    [Column("path")]
    [Comment("Webhook 路径")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// HTTP 方法。
    /// </summary>
    [Required]
    [MaxLength(16)]
    [Column("method")]
    [Comment("HTTP 方法")]
    public string Method { get; set; } = "POST";

    /// <summary>
    /// 关联工作流定义 ID。
    /// </summary>
    [Required]
    [Column("workflow_definition_id")]
    [Comment("关联工作流定义 ID")]
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>
    /// 触发器 ID。
    /// </summary>
    [Required]
    [Column("trigger_id")]
    [Comment("触发器 ID")]
    public Guid TriggerId { get; set; }

    /// <summary>
    /// 是否静态路由（工作流创建时生成）。
    /// </summary>
    [Column("is_static")]
    [Comment("是否静态路由")]
    public bool IsStatic { get; set; }

    /// <summary>
    /// 签名密钥（HMAC-SHA256）。
    /// </summary>
    [MaxLength(512)]
    [Column("secret")]
    [Comment("签名密钥")]
    public string? Secret { get; set; }

    /// <summary>
    /// IP 白名单 JSON。
    /// </summary>
    [Column("allowed_ips")]
    [Comment("IP 白名单 JSON")]
    public string? AllowedIpsJson { get; set; }

    /// <summary>
    /// 来源域白名单 JSON。
    /// </summary>
    [Column("allowed_origins")]
    [Comment("来源域白名单 JSON")]
    public string? AllowedOriginsJson { get; set; }

    /// <summary>
    /// 是否同步响应（等待执行完成）。
    /// </summary>
    [Column("is_sync")]
    [Comment("是否同步响应")]
    public bool IsSync { get; set; }

    /// <summary>
    /// 同步响应最大等待时间（秒）。
    /// </summary>
    [Column("max_wait_seconds")]
    [Comment("同步响应最大等待时间（秒）")]
    public int MaxWaitSeconds { get; set; } = 30;
}
