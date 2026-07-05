using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Triggers;

/// <summary>
/// Webhook 路由管理服务，从 TriggerService 中提取，负责 Webhook 路由的创建、更新、删除和校验。
/// </summary>
public sealed class WebhookRouteService(FlowEngineDbContext dbContext)
{
    /// <summary>
    /// 为触发器创建 Webhook 路由。
    /// </summary>
    public async Task ApplyRouteAsync(Trigger trigger, TriggerSettings settings, CancellationToken cancellationToken)
    {
        if (trigger.Type != TriggerType.Webhook)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.WebhookPath))
        {
            return;
        }

        await ValidatePathAsync(settings.WebhookPath, excludeTriggerId: null, cancellationToken)
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

    /// <summary>
    /// 更新触发器的 Webhook 路由。
    /// </summary>
    public async Task UpdateRouteAsync(Trigger trigger, TriggerSettings settings, CancellationToken cancellationToken)
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

        await ValidatePathAsync(settings.WebhookPath, trigger.Id, cancellationToken)
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

    /// <summary>
    /// 删除指定触发器关联的所有 Webhook 路由。
    /// </summary>
    public async Task RemoveRoutesByTriggerIdAsync(Guid triggerId, CancellationToken cancellationToken)
    {
        var routes = await dbContext.WebhookRoutes
            .Where(r => r.TriggerId == triggerId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        dbContext.WebhookRoutes.RemoveRange(routes);
    }

    /// <summary>
    /// 删除指定工作流关联的所有 Webhook 路由。
    /// </summary>
    public async Task RemoveRoutesByWorkflowIdAsync(Guid workflowDefinitionId, CancellationToken cancellationToken)
    {
        var routes = await dbContext.WebhookRoutes
            .Where(r => r.WorkflowDefinitionId == workflowDefinitionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        dbContext.WebhookRoutes.RemoveRange(routes);
    }

    /// <summary>
    /// 校验 Webhook 路径唯一性。
    /// </summary>
    public async Task ValidatePathAsync(string path, Guid? excludeTriggerId, CancellationToken cancellationToken)
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
            throw new BusinessException($"Webhook path '{path}' is already in use.");
        }
    }
}
