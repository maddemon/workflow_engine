using System.Text.Json;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Application.Triggers;

/// <summary>
/// 触发器应用服务。
/// </summary>
public sealed class TriggerService(
    FlowEngineDbContext dbContext,
    IEventBus eventBus,
    AuditEventFactory auditFactory,
    IScheduleManager scheduleManager,
    IAuthorizationGuard authGuard,
    WebhookRouteService webhookRouteService,
    ILogger<TriggerService> logger)
{
    /// <summary>
    /// 创建触发器。
    /// </summary>
    public async Task<TriggerDto> CreateAsync(CreateTriggerDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        await authGuard.RequireScopeAsync(Scope.Trigger, Operation.Write, cancellationToken);

        var workflow = await dbContext.Workflows
            .FirstOrDefaultAsync(w => w.Id == dto.WorkflowDefinitionId, cancellationToken)
            .ConfigureAwait(false);
        if (workflow is null)
        {
            throw new NotFoundException("关联的工作流不存在。");
        }

        var triggerSettings = dto.Settings is not null ? ConvertToTriggerSettings(dto.Settings) : new TriggerSettings();
        var trigger = new Trigger
        {
            WorkflowDefinitionId = dto.WorkflowDefinitionId,
            ProjectId = workflow.ProjectId,
            WorkflowVersion = dto.WorkflowVersion,
            Type = dto.Type,
            Name = dto.Name,
            IsActive = dto.IsActive,
            Settings = triggerSettings
        };


        if (dto.Type == TriggerType.Webhook)
        {
            await webhookRouteService.ApplyRouteAsync(trigger, triggerSettings, cancellationToken).ConfigureAwait(false);
        }

        dbContext.Triggers.Add(trigger);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (dto.Type == TriggerType.Poll)
        {
            await RegisterPollTriggerAsync(trigger, triggerSettings, cancellationToken).ConfigureAwait(false);
        }

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
        await authGuard.RequireAccessAsync(ResourceKind.Trigger, id, Operation.Read, cancellationToken);

        var trigger = await dbContext.Triggers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (trigger is null)
        {
            return null;
        }

        return MapToDto(trigger);
    }

    /// <summary>
    /// 按工作流定义 ID 获取触发器列表。
    /// </summary>
    public async Task<IReadOnlyCollection<TriggerDto>> GetByWorkflowDefinitionIdAsync(
        Guid workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        // RBAC：查询工作流触发器列表前校验工作流读权限。
        await authGuard.RequireAccessAsync(ResourceKind.Workflow, workflowDefinitionId, Operation.Read, cancellationToken);

        var triggers = await dbContext.Triggers
            .AsNoTracking()
            .Where(t => t.WorkflowDefinitionId == workflowDefinitionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return triggers.Select(MapToDto).ToList();
    }

    /// <summary>
    /// 获取所有触发器。项目仅用于分类，不对触发器可见性做隔离。
    /// </summary>
    public async Task<IReadOnlyCollection<TriggerDto>> GetAllForUserAsync(
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Triggers.AsNoTracking();
        if (projectId.HasValue)
        {
            query = query.Where(t => t.ProjectId == projectId.Value);
        }

        var triggers = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        return triggers.Select(MapToDto).ToList();
    }

    /// <summary>
    /// 更新触发器。
    /// </summary>
    public async Task<TriggerDto?> UpdateAsync(
        Guid id, UpdateTriggerDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        await authGuard.RequireAccessAsync(ResourceKind.Trigger, id, Operation.Write, cancellationToken);
        await authGuard.RequireScopeAsync(Scope.Trigger, Operation.Write, cancellationToken);

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
            await webhookRouteService.UpdateRouteAsync(trigger, trigger.Settings, cancellationToken).ConfigureAwait(false);
        }

        // 先注销：Poll 触发器先注销旧调度。
        if (trigger.Type == TriggerType.Poll)
        {
            try
            {
                await UnregisterPollTriggerAsync(trigger.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "触发器 {TriggerId} 注销旧调度失败，继续更新数据库。", trigger.Id);
            }
        }

        // 数据库事务包裹：仅包裹 SaveChanges（Quartz 调度为外部状态，置于事务外）。
        // InMemory 测试提供程序不支持事务，仅在关系型提供程序下开启。
        if (dbContext.Database.IsRelational())
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        // 注册新调度：SaveChanges 成功后，尝试注册新调度（Quartz 外部状态，在事务外）。
        if (trigger.Type == TriggerType.Poll && trigger.IsActive)
        {
            try
            {
                await RegisterPollTriggerAsync(trigger, trigger.Settings, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 补偿日志：注册新调度失败，数据库已保存但调度未恢复，需人工补偿。
                logger.LogError(ex,
                    "触发器 {TriggerId} 更新后注册新调度失败，数据库已保存但调度未恢复，需人工补偿。",
                    trigger.Id);
            }
        }

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
        await authGuard.RequireAccessAsync(ResourceKind.Trigger, id, Operation.Delete, cancellationToken);
        await authGuard.RequireAdminAsync(Operation.Delete, cancellationToken);

        var trigger = await dbContext.Triggers
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (trigger is null)
        {
            return false;
        }

        // Webhook 路由在删除触发器前加载，确保在同一 SaveChanges 中一并删除。
        if (trigger.Type == TriggerType.Webhook)
        {
            await webhookRouteService.RemoveRoutesByTriggerIdAsync(id, cancellationToken).ConfigureAwait(false);
        }

        // 先注销：Poll 触发器先注销调度。
        if (trigger.Type == TriggerType.Poll)
        {
            try
            {
                await UnregisterPollTriggerAsync(trigger.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "触发器 {TriggerId} 注销调度失败，继续删除数据库记录。", trigger.Id);
            }
        }

        dbContext.Triggers.Remove(trigger);

        // 数据库事务包裹：仅包裹 SaveChanges（Quartz 调度为外部状态，置于事务外）。
        // InMemory 测试提供程序不支持事务，仅在关系型提供程序下开启。
        if (dbContext.Database.IsRelational())
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        else
        {
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

        await webhookRouteService.RemoveRoutesByWorkflowIdAsync(workflowDefinitionId, cancellationToken).ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 注册工作流关联的所有调度触发器。
    /// </summary>
    public async Task RegisterWorkflowSchedulesAsync(Guid workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var triggers = await dbContext.Triggers
            .Where(t => t.WorkflowDefinitionId == workflowDefinitionId && t.IsActive)
            .AsNoTracking()
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
            .AsNoTracking()
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
            .AsNoTracking()
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

    /// <summary>
    /// 判断当前用户是否有触发器写权限（系统全局角色 Admin/Editor）。
    /// </summary>
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
            IntervalSeconds = dto.IntervalSeconds,
            TimeoutSeconds = dto.TimeoutSeconds,
            PollNodeId = dto.PollNodeId,
            DedupStrategy = dto.DedupStrategy,
            SkipIfRunning = dto.SkipIfRunning,
            LastPollId = dto.LastPollId,
            LastPollTime = dto.LastPollTime,
        };
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
            IntervalSeconds = settings.IntervalSeconds,
            TimeoutSeconds = settings.TimeoutSeconds,
            PollNodeId = settings.PollNodeId,
            DedupStrategy = settings.DedupStrategy,
            SkipIfRunning = settings.SkipIfRunning,
            LastPollId = settings.LastPollId,
            LastPollTime = settings.LastPollTime,
        };
    }

    private async Task RegisterPollTriggerAsync(
        Trigger trigger,
        TriggerSettings settings,
        CancellationToken cancellationToken)
    {
        if (trigger.Type != TriggerType.Poll)
        {
            return;
        }

        if (!trigger.IsActive)
        {
            return;
        }

        await scheduleManager.RegisterPollTriggerAsync(
            trigger.Id,
            trigger.WorkflowDefinitionId,
            settings.IntervalSeconds,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task UnregisterPollTriggerAsync(Guid triggerId, CancellationToken cancellationToken)
    {
        await scheduleManager.UnregisterPollTriggerAsync(triggerId, cancellationToken).ConfigureAwait(false);
    }
}
