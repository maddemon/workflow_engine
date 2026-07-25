using System.Text.Json;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Abstractions;
using Mapster;
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

        var triggerSettings = dto.Settings?.Adapt<TriggerSettings>() ?? new TriggerSettings();
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

        // 触发器与其 Webhook 路由需原子落库（InMemory 不支持事务，仅关系型下开启）。
        // CQ-4：关系型下以事务包裹 SaveChanges，InMemory 直接保存（详见 SaveChangesInTransactionAsync）。
        await SaveChangesInTransactionAsync(cancellationToken).ConfigureAwait(false);

        if (dto.Type == TriggerType.Poll)
        {
            try
            {
                await RegisterPollTriggerAsync(trigger, triggerSettings, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // EX-3：注册失败后补偿（回退为非激活）+ 告警，杜绝“已激活但调度未注册”的静默失效。
                await CompensateRegistrationFailureAsync(trigger, ex, cancellationToken).ConfigureAwait(false);
            }
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

        return trigger.Adapt<TriggerDto>();
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

        return trigger.Adapt<TriggerDto>();
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

        return triggers.Select(t => t.Adapt<TriggerDto>()).ToList();
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
        return triggers.Select(t => t.Adapt<TriggerDto>()).ToList();
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
        trigger.Settings = dto.Settings?.Adapt<TriggerSettings>() ?? new TriggerSettings();
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
        // CQ-4：关系型下以事务包裹 SaveChanges，InMemory 直接保存（详见 SaveChangesInTransactionAsync）。
        await SaveChangesInTransactionAsync(cancellationToken).ConfigureAwait(false);

        // 注册新调度：SaveChanges 成功后，尝试注册新调度（Quartz 外部状态，在事务外）。
        if (trigger.Type == TriggerType.Poll && trigger.IsActive)
        {
            try
            {
                await RegisterPollTriggerAsync(trigger, trigger.Settings, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // EX-3：注册失败后补偿（回退为非激活）+ 告警，杜绝“已激活但调度未注册”的静默失效。
                await CompensateRegistrationFailureAsync(trigger, ex, cancellationToken).ConfigureAwait(false);
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

        return trigger.Adapt<TriggerDto>();
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
        // CQ-4：关系型下以事务包裹 SaveChanges，InMemory 直接保存（详见 SaveChangesInTransactionAsync）。
        await SaveChangesInTransactionAsync(cancellationToken).ConfigureAwait(false);

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

        // Webhook 路由与触发器同属一个工作流。本方法常被 WorkflowService.DeleteAsync 在既有事务内调用，
        // 故不再开启嵌套事务（关系型提供程序不支持嵌套事务）；独立调用时 ExecuteDeleteAsync 自身为原子单语句。
        await webhookRouteService.RemoveRoutesByWorkflowIdAsync(workflowDefinitionId, cancellationToken).ConfigureAwait(false);

        if (dbContext.Database.IsRelational())
        {
            // ExecuteDeleteAsync：一次往返删除，不物化整行触发器实体。
            // IgnoreQueryFilters：删除工作流需清除其全部触发器（含被级联软删除的），不受全局软删除过滤影响。
            await dbContext.Triggers
                .IgnoreQueryFilters()
                .Where(t => t.WorkflowDefinitionId == workflowDefinitionId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var triggers = await dbContext.Triggers
                .IgnoreQueryFilters()
                .Where(t => t.WorkflowDefinitionId == workflowDefinitionId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            dbContext.Triggers.RemoveRange(triggers);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
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
    /// 注销项目关联的所有调度与轮询触发器（用于项目级联软删前清理外部 Quartz 调度）。
    /// 使用 <c>IgnoreQueryFilters</c> 以便清理已被级联软删（<c>Deleted=true</c>）的触发器，
    /// 避免工作流被软删后调度残留、ExecutionService 加载为 null 而静默 no-op（GAP 1 / D-1 / EX-3）。
    /// </summary>
    public async Task UnregisterProjectSchedulesAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var triggers = await dbContext.Triggers
            .IgnoreQueryFilters()
            .Where(t => t.ProjectId == projectId)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var trigger in triggers)
        {
            if (trigger.Type == TriggerType.Schedule)
            {
                await scheduleManager.UnregisterScheduleAsync(trigger.Id, cancellationToken).ConfigureAwait(false);
            }
            else if (trigger.Type == TriggerType.Poll)
            {
                await scheduleManager.UnregisterPollTriggerAsync(trigger.Id, cancellationToken).ConfigureAwait(false);
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
        return triggers.Select(t => t.Adapt<TriggerDto>()).ToList();
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

    /// <summary>
    /// 触发器调度注册失败后的补偿：将触发器回退为非激活并告警，使数据库状态与调度器实际状态一致
    /// （避免“已激活但调度未注册”的静默失效）。回退本身失败仅告警，不掩盖原始调度异常。
    /// </summary>
    private async Task CompensateRegistrationFailureAsync(Trigger trigger, Exception ex, CancellationToken cancellationToken)
    {
        logger.LogError(ex,
            "触发器 {TriggerId} 注册调度失败，回退为未激活并告警，需人工重新激活。",
            trigger.Id);

        try
        {
            trigger.IsActive = false;
            trigger.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception saveEx)
        {
            logger.LogError(saveEx, "触发器 {TriggerId} 注册失败补偿（回退为非激活）失败，需人工干预。", trigger.Id);
        }
    }

    /// <summary>
    /// CQ-4：以事务包裹 <see cref="FlowEngineDbContext.SaveChangesAsync"/> 的通用模板。
    /// 关系型提供程序下开启事务并提交，提交失败自动回滚并重新抛出；
    /// InMemory 提供程序不支持事务，仅直接保存（触发器/Webhook 路由的原子性在关系型下由事务保证）。
    /// </summary>
    private async Task SaveChangesInTransactionAsync(CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

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