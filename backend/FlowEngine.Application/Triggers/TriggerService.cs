using System.Text.Json;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Triggers;

/// <summary>
/// 触发器应用服务。
/// </summary>
public sealed class TriggerService(
    FlowEngineDbContext dbContext,
    IEventBus eventBus,
    AuditEventFactory auditFactory,
    IScheduleManager scheduleManager)
{
    /// <summary>
    /// 创建触发器。
    /// </summary>
    public async Task<TriggerDto> CreateAsync(CreateTriggerDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var triggerSettings = dto.Settings is not null ? ConvertToTriggerSettings(dto.Settings) : new TriggerSettings();
        var trigger = new Trigger
        {
            WorkflowDefinitionId = dto.WorkflowDefinitionId,
            WorkflowVersion = dto.WorkflowVersion,
            Type = dto.Type,
            Name = dto.Name,
            IsActive = dto.IsActive,
            Settings = triggerSettings
        };


        if (dto.Type == TriggerType.Webhook)
        {
            await ApplyWebhookRouteAsync(trigger, triggerSettings, cancellationToken).ConfigureAwait(false);
        }

        dbContext.Triggers.Add(trigger);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
            AuditEventTypes.TriggerCreated,
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
        var trigger = await dbContext.Triggers
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return trigger is null ? null : MapToDto(trigger);
    }

    /// <summary>
    /// 按工作流定义 ID 获取触发器列表。
    /// </summary>
    public async Task<IReadOnlyCollection<TriggerDto>> GetByWorkflowDefinitionIdAsync(
        Guid workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var triggers = await dbContext.Triggers
            .Where(t => t.WorkflowDefinitionId == workflowDefinitionId)
            .ToListAsync(cancellationToken)
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

        var trigger = await dbContext.Triggers
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (trigger is null)
        {
            return null;
        }

        trigger.Name = dto.Name;
        trigger.IsActive = dto.IsActive;
        trigger.Settings = dto.Settings is not null ? ConvertToTriggerSettings(dto.Settings) : new TriggerSettings();
        trigger.UpdatedAt = DateTime.UtcNow;

        if (trigger.Type == TriggerType.Webhook)
        {
            await UpdateWebhookRouteAsync(trigger, trigger.Settings, cancellationToken).ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
            AuditEventTypes.TriggerUpdated,
            "Trigger",
            trigger.Id,
            new Dictionary<string, object>
            {
                ["triggerType"] = trigger.Type.ToString(),
                ["workflowDefinitionId"] = trigger.WorkflowDefinitionId,
                ["isActive"] = trigger.IsActive,
            }),
            cancellationToken).ConfigureAwait(false);

        return MapToDto(trigger);
    }

    /// <summary>
    /// 删除触发器。
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var trigger = await dbContext.Triggers
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (trigger is null)
        {
            return false;
        }

        dbContext.Triggers.Remove(trigger);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (trigger.Type == TriggerType.Webhook)
        {
            var routes = await dbContext.WebhookRoutes
                .Where(r => r.TriggerId == id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            dbContext.WebhookRoutes.RemoveRange(routes);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
            AuditEventTypes.TriggerDeleted,
            "Trigger",
            trigger.Id,
            new Dictionary<string, object>
            {
                ["triggerType"] = trigger.Type.ToString(),
                ["workflowDefinitionId"] = trigger.WorkflowDefinitionId,
            }),
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// 删除工作流关联的所有触发器。
    /// </summary>
    public async Task DeleteByWorkflowDefinitionIdAsync(
        Guid workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        await UnregisterWorkflowSchedulesAsync(workflowDefinitionId, cancellationToken).ConfigureAwait(false);

        var triggers = await dbContext.Triggers
            .Where(t => t.WorkflowDefinitionId == workflowDefinitionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        dbContext.Triggers.RemoveRange(triggers);

        var routes = await dbContext.WebhookRoutes
            .Where(r => r.WorkflowDefinitionId == workflowDefinitionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        dbContext.WebhookRoutes.RemoveRange(routes);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 注册工作流关联的所有调度触发器。
    /// </summary>
    public async Task RegisterWorkflowSchedulesAsync(Guid workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var triggers = await dbContext.Triggers
            .Where(t => t.WorkflowDefinitionId == workflowDefinitionId && t.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var trigger in triggers)
        {
            if (trigger.Type == TriggerType.Schedule)
            {
                var settings = trigger.Settings;

                if (settings?.CronExpression is not null)
                {
                    await scheduleManager.RegisterScheduleAsync(
                        trigger.Id,
                        workflowDefinitionId,
                        settings.CronExpression,
                        settings.TimeZone,
                        settings.StartAt,
                        settings.EndAt,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// 注销工作流关联的所有调度触发器。
    /// </summary>
    public async Task UnregisterWorkflowSchedulesAsync(Guid workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var triggers = await dbContext.Triggers
            .Where(t => t.WorkflowDefinitionId == workflowDefinitionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var trigger in triggers)
        {
            if (trigger.Type == TriggerType.Schedule)
            {
                await scheduleManager.UnregisterScheduleAsync(trigger.Id, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 获取所有激活的触发器（用于启动时恢复调度）。
    /// </summary>
    public async Task<IReadOnlyCollection<TriggerDto>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var triggers = await dbContext.Triggers
            .Where(t => t.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return triggers.Select(MapToDto).ToList();
    }

    /// <summary>
    /// 更新触发器最后触发时间和下次触发时间。
    /// </summary>
    public async Task UpdateTriggerTimestampsAsync(
        Guid triggerId, DateTime lastTriggeredAt, DateTime? nextTriggerAt, CancellationToken cancellationToken = default)
    {
        var trigger = await dbContext.Triggers
            .FirstOrDefaultAsync(t => t.Id == triggerId, cancellationToken)
            .ConfigureAwait(false);
        if (trigger is null) return;

        trigger.LastTriggeredAt = lastTriggeredAt;
        trigger.NextTriggerAt = nextTriggerAt;
        trigger.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static TriggerDto MapToDto(Trigger trigger)
    {
        TriggerSettingsDto? settings = null;
        if (trigger.Settings is not null)
        {
            settings = ConvertToTriggerSettingsDto(trigger.Settings);
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

    private static TriggerSettings ConvertToTriggerSettings(TriggerSettingsDto dto)
    {
        return new TriggerSettings
        {
            CronExpression = dto.CronExpression,
            TimeZone = dto.TimeZone,
            StartAt = dto.StartAt,
            EndAt = dto.EndAt,
            WebhookPath = dto.WebhookPath,
            Secret = dto.Secret,
            AllowedIps = dto.AllowedIps,
            AllowedOrigins = dto.AllowedOrigins,
            IsSync = dto.IsSync,
            MaxWaitSeconds = dto.MaxWaitSeconds,
        };
    }

    private async Task ApplyWebhookRouteAsync(
        Trigger trigger,
        TriggerSettings settings,
        CancellationToken cancellationToken)
    {
        if (trigger.Type != TriggerType.Webhook)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.WebhookPath))
        {
            return;
        }

        await ValidateWebhookPathAsync(settings.WebhookPath, excludeTriggerId: null, cancellationToken)
            .ConfigureAwait(false);

        var route = new WebhookRoute
        {
            Path = settings.WebhookPath,
            Method = "POST",
            WorkflowDefinitionId = trigger.WorkflowDefinitionId,
            TriggerId = trigger.Id,
            IsStatic = false,
            Secret = settings.Secret,
            AllowedIps = settings.AllowedIps,
            AllowedOrigins = settings.AllowedOrigins,
            IsSync = settings.IsSync,
            MaxWaitSeconds = settings.MaxWaitSeconds,
        };

        dbContext.WebhookRoutes.Add(route);
    }

    private async Task UpdateWebhookRouteAsync(
        Trigger trigger,
        TriggerSettings settings,
        CancellationToken cancellationToken)
    {
        if (trigger.Type != TriggerType.Webhook)
        {
            return;
        }

        var existingRoutes = await dbContext.WebhookRoutes
            .Where(r => r.TriggerId == trigger.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(settings.WebhookPath))
        {
            dbContext.WebhookRoutes.RemoveRange(existingRoutes);
            return;
        }

        await ValidateWebhookPathAsync(settings.WebhookPath, trigger.Id, cancellationToken)
            .ConfigureAwait(false);

        var route = existingRoutes.FirstOrDefault();
        if (route is null)
        {
            route = new WebhookRoute
            {
                Method = "POST",
                WorkflowDefinitionId = trigger.WorkflowDefinitionId,
                TriggerId = trigger.Id,
                IsStatic = false,
            };
            dbContext.WebhookRoutes.Add(route);
        }

        route.Path = settings.WebhookPath;
        route.Secret = settings.Secret;
        route.AllowedIps = settings.AllowedIps;
        route.AllowedOrigins = settings.AllowedOrigins;
        route.IsSync = settings.IsSync;
        route.MaxWaitSeconds = settings.MaxWaitSeconds;
    }

    private async Task ValidateWebhookPathAsync(
        string path,
        Guid? excludeTriggerId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.WebhookRoutes
            .Where(r => r.Path == path);

        if (excludeTriggerId.HasValue)
        {
            query = query.Where(r => r.TriggerId != excludeTriggerId.Value);
        }

        var exists = await query.AnyAsync(cancellationToken).ConfigureAwait(false);
        if (exists)
        {
            throw new InvalidOperationException($"Webhook path '{path}' is already in use.");
        }
    }

    private static TriggerSettingsDto ConvertToTriggerSettingsDto(TriggerSettings settings)
    {
        return new TriggerSettingsDto
        {
            CronExpression = settings.CronExpression,
            TimeZone = settings.TimeZone,
            StartAt = settings.StartAt,
            EndAt = settings.EndAt,
            WebhookPath = settings.WebhookPath,
            Secret = settings.Secret,
            AllowedIps = settings.AllowedIps,
            AllowedOrigins = settings.AllowedOrigins,
            IsSync = settings.IsSync,
            MaxWaitSeconds = settings.MaxWaitSeconds,
        };
    }
}
