using System.Text.Json;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;

namespace FlowEngine.Application.Triggers;

/// <summary>
/// 触发器应用服务。
/// </summary>
public sealed class TriggerService
{
    private readonly ITriggerRepository _triggerRepository;
    private readonly IEventBus _eventBus;
    private readonly AuditEventFactory _auditFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// 初始化触发器应用服务。
    /// </summary>
    public TriggerService(
        ITriggerRepository triggerRepository,
        IEventBus eventBus,
        AuditEventFactory auditFactory)
    {
        _triggerRepository = triggerRepository ?? throw new ArgumentNullException(nameof(triggerRepository));
        _eventBus = eventBus;
        _auditFactory = auditFactory;
    }

    /// <summary>
    /// 创建触发器。
    /// </summary>
    public async Task<TriggerDto> CreateAsync(CreateTriggerDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var trigger = new Trigger
        {
            WorkflowDefinitionId = dto.WorkflowDefinitionId,
            WorkflowVersion = dto.WorkflowVersion,
            Type = dto.Type,
            Name = dto.Name,
            IsActive = dto.IsActive,
            SettingsJson = dto.Settings is not null
                ? JsonSerializer.Serialize(dto.Settings, JsonOptions)
                : "{}",
        };

        await _triggerRepository.SaveAsync(trigger, cancellationToken).ConfigureAwait(false);

        if (dto.Type == TriggerType.Webhook && dto.Settings?.WebhookPath is not null)
        {
            var route = new WebhookRoute
            {
                Path = dto.Settings.WebhookPath,
                Method = "POST",
                WorkflowDefinitionId = dto.WorkflowDefinitionId,
                TriggerId = trigger.Id,
                IsStatic = false,
                Secret = dto.Settings.Secret,
                AllowedIpsJson = dto.Settings.AllowedIps is not null
                    ? JsonSerializer.Serialize(dto.Settings.AllowedIps, JsonOptions)
                    : null,
                AllowedOriginsJson = dto.Settings.AllowedOrigins is not null
                    ? JsonSerializer.Serialize(dto.Settings.AllowedOrigins, JsonOptions)
                    : null,
                IsSync = dto.Settings.IsSync,
                MaxWaitSeconds = dto.Settings.MaxWaitSeconds,
            };

            await _triggerRepository.SaveWebhookRouteAsync(route, cancellationToken).ConfigureAwait(false);
        }

        await _eventBus.PublishAsync(_auditFactory.Create<AuditLogEvent>(
            "Trigger.Created",
            "Trigger",
            trigger.Id,
            new Dictionary<string, object>
            {
                ["triggerType"] = trigger.Type.ToString(),
                ["workflowDefinitionId"] = trigger.WorkflowDefinitionId,
            }),
            cancellationToken).ConfigureAwait(false);

        return MapToDto(trigger);
    }

    /// <summary>
    /// 按 ID 获取触发器。
    /// </summary>
    public async Task<TriggerDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var trigger = await _triggerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return trigger is null ? null : MapToDto(trigger);
    }

    /// <summary>
    /// 按工作流定义 ID 获取触发器列表。
    /// </summary>
    public async Task<IReadOnlyCollection<TriggerDto>> GetByWorkflowDefinitionIdAsync(
        Guid workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var triggers = await _triggerRepository
            .GetByWorkflowDefinitionIdAsync(workflowDefinitionId, cancellationToken)
            .ConfigureAwait(false);

        return triggers.Select(MapToDto).ToList();
    }

    /// <summary>
    /// 更新触发器。
    /// </summary>
    public async Task<TriggerDto?> UpdateAsync(
        Guid id, UpdateTriggerDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var trigger = await _triggerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (trigger is null)
        {
            return null;
        }

        trigger.Name = dto.Name;
        trigger.IsActive = dto.IsActive;
        trigger.SettingsJson = dto.Settings is not null
            ? JsonSerializer.Serialize(dto.Settings, JsonOptions)
            : trigger.SettingsJson;
        trigger.UpdatedAt = DateTime.UtcNow;

        await _triggerRepository.SaveAsync(trigger, cancellationToken).ConfigureAwait(false);

        return MapToDto(trigger);
    }

    /// <summary>
    /// 删除触发器。
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var trigger = await _triggerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (trigger is null)
        {
            return false;
        }

        await _triggerRepository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

        if (trigger.Type == TriggerType.Webhook)
        {
            await _triggerRepository
                .DeleteWebhookRoutesByTriggerIdAsync(id, cancellationToken)
                .ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// 删除工作流关联的所有触发器。
    /// </summary>
    public async Task DeleteByWorkflowDefinitionIdAsync(
        Guid workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        await _triggerRepository
            .DeleteByWorkflowDefinitionIdAsync(workflowDefinitionId, cancellationToken)
            .ConfigureAwait(false);

        await _triggerRepository
            .DeleteWebhookRoutesByWorkflowDefinitionIdAsync(workflowDefinitionId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 获取所有激活的触发器（用于启动时恢复调度）。
    /// </summary>
    public async Task<IReadOnlyCollection<TriggerDto>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var triggers = await _triggerRepository.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        return triggers.Select(MapToDto).ToList();
    }

    /// <summary>
    /// 更新触发器最后触发时间和下次触发时间。
    /// </summary>
    public async Task UpdateTriggerTimestampsAsync(
        Guid triggerId, DateTime lastTriggeredAt, DateTime? nextTriggerAt, CancellationToken cancellationToken = default)
    {
        var trigger = await _triggerRepository.GetByIdAsync(triggerId, cancellationToken).ConfigureAwait(false);
        if (trigger is null) return;

        trigger.LastTriggeredAt = lastTriggeredAt;
        trigger.NextTriggerAt = nextTriggerAt;
        trigger.UpdatedAt = DateTime.UtcNow;

        await _triggerRepository.SaveAsync(trigger, cancellationToken).ConfigureAwait(false);
    }

    private static TriggerDto MapToDto(Trigger trigger)
    {
        TriggerSettingsDto? settings = null;
        if (!string.IsNullOrEmpty(trigger.SettingsJson) && trigger.SettingsJson != "{}")
        {
            settings = JsonSerializer.Deserialize<TriggerSettingsDto>(trigger.SettingsJson, JsonOptions);
        }

        return new TriggerDto
        {
            Id = trigger.Id,
            WorkflowDefinitionId = trigger.WorkflowDefinitionId,
            WorkflowVersion = trigger.WorkflowVersion,
            Type = trigger.Type,
            Name = trigger.Name,
            IsActive = trigger.IsActive,
            Settings = settings,
            LastTriggeredAt = trigger.LastTriggeredAt,
            NextTriggerAt = trigger.NextTriggerAt,
        };
    }
}
