using FlowEngine.Core.Enums;

namespace FlowEngine.Application.Dtos;

/// <summary>
/// 触发器 DTO。
/// </summary>
public sealed class TriggerDto
{
    /// <summary>
    /// 触发器 ID。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 关联工作流定义 ID。
    /// </summary>
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>
    /// 工作流版本号。
    /// </summary>
    public int WorkflowVersion { get; set; }

    /// <summary>
    /// 触发器类型。
    /// </summary>
    public TriggerType Type { get; set; }

    /// <summary>
    /// 触发器名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 是否激活。
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// 触发器配置。
    /// </summary>
    public TriggerSettingsDto? Settings { get; set; }

    /// <summary>
    /// 最后触发时间。
    /// </summary>
    public DateTime? LastTriggeredAt { get; set; }

    /// <summary>
    /// 下次触发时间。
    /// </summary>
    public DateTime? NextTriggerAt { get; set; }
}

/// <summary>
/// 创建触发器请求 DTO。
/// </summary>
public sealed class CreateTriggerDto
{
    /// <summary>
    /// 关联工作流定义 ID。
    /// </summary>
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>
    /// 工作流版本号。
    /// </summary>
    public int WorkflowVersion { get; set; }

    /// <summary>
    /// 触发器类型。
    /// </summary>
    public TriggerType Type { get; set; }

    /// <summary>
    /// 触发器名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 触发器配置。
    /// </summary>
    public TriggerSettingsDto? Settings { get; set; }
}

/// <summary>
/// 更新触发器请求 DTO。
/// </summary>
public sealed class UpdateTriggerDto
{
    /// <summary>
    /// 触发器名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 是否激活。
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// 触发器配置。
    /// </summary>
    public TriggerSettingsDto? Settings { get; set; }
}

/// <summary>
/// 触发器配置 DTO。
/// </summary>
public sealed class TriggerSettingsDto
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

/// <summary>
/// Webhook 路由 DTO。
/// </summary>
public sealed class WebhookRouteDto
{
    /// <summary>
    /// 路由 ID。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Webhook 路径。
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// HTTP 方法。
    /// </summary>
    public string Method { get; set; } = "POST";

    /// <summary>
    /// 关联工作流定义 ID。
    /// </summary>
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>
    /// 触发器 ID。
    /// </summary>
    public Guid TriggerId { get; set; }
}
