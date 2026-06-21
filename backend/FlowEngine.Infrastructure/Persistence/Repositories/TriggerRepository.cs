using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Infrastructure.Persistence.Repositories;

/// <summary>
/// 触发器仓储实现。
/// </summary>
public sealed class TriggerRepository : ITriggerRepository
{
    private readonly FlowEngineDbContext _context;

    /// <summary>
    /// 初始化触发器仓储。
    /// </summary>
    public TriggerRepository(FlowEngineDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<Trigger?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Set<TriggerEntity>()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : MapToDomain(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<Trigger>> GetByWorkflowDefinitionIdAsync(
        Guid workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var entities = await _context.Set<TriggerEntity>()
            .Where(x => x.WorkflowDefinitionId == workflowDefinitionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(MapToDomain).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<Trigger>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _context.Set<TriggerEntity>()
            .Where(x => x.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(MapToDomain).ToList();
    }

    /// <inheritdoc />
    public async Task SaveAsync(Trigger trigger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        var existing = await _context.Set<TriggerEntity>()
            .FirstOrDefaultAsync(x => x.Id == trigger.Id, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            _context.Entry(existing).CurrentValues.SetValues(MapToEntity(trigger));
        }
        else
        {
            _context.Set<TriggerEntity>().Add(MapToEntity(trigger));
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Set<TriggerEntity>()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is not null)
        {
            _context.Set<TriggerEntity>().Remove(entity);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task DeleteByWorkflowDefinitionIdAsync(Guid workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var entities = await _context.Set<TriggerEntity>()
            .Where(x => x.WorkflowDefinitionId == workflowDefinitionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entities.Count > 0)
        {
            _context.Set<TriggerEntity>().RemoveRange(entities);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<WebhookRoute>> GetWebhookRoutesByWorkflowDefinitionIdAsync(
        Guid workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var entities = await _context.Set<WebhookRouteEntity>()
            .Where(x => x.WorkflowDefinitionId == workflowDefinitionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(MapRouteToDomain).ToList();
    }

    /// <inheritdoc />
    public async Task<WebhookRoute?> GetWebhookRouteByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Set<WebhookRouteEntity>()
            .FirstOrDefaultAsync(x => x.Path == path, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : MapRouteToDomain(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<WebhookRoute>> GetAllWebhookRoutesAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _context.Set<WebhookRouteEntity>()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(MapRouteToDomain).ToList();
    }

    /// <inheritdoc />
    public async Task SaveWebhookRouteAsync(WebhookRoute route, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);

        var existing = await _context.Set<WebhookRouteEntity>()
            .FirstOrDefaultAsync(x => x.Id == route.Id, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            _context.Entry(existing).CurrentValues.SetValues(MapRouteToEntity(route));
        }
        else
        {
            _context.Set<WebhookRouteEntity>().Add(MapRouteToEntity(route));
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteWebhookRouteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Set<WebhookRouteEntity>()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is not null)
        {
            _context.Set<WebhookRouteEntity>().Remove(entity);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task DeleteWebhookRoutesByWorkflowDefinitionIdAsync(
        Guid workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        var entities = await _context.Set<WebhookRouteEntity>()
            .Where(x => x.WorkflowDefinitionId == workflowDefinitionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entities.Count > 0)
        {
            _context.Set<WebhookRouteEntity>().RemoveRange(entities);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task DeleteWebhookRoutesByTriggerIdAsync(
        Guid triggerId, CancellationToken cancellationToken = default)
    {
        var entities = await _context.Set<WebhookRouteEntity>()
            .Where(x => x.TriggerId == triggerId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entities.Count > 0)
        {
            _context.Set<WebhookRouteEntity>().RemoveRange(entities);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static Trigger MapToDomain(TriggerEntity entity)
    {
        return new Trigger
        {
            Id = entity.Id,
            WorkflowDefinitionId = entity.WorkflowDefinitionId,
            WorkflowVersion = entity.WorkflowVersion,
            Type = entity.Type,
            Name = entity.Name,
            IsActive = entity.IsActive,
            SettingsJson = entity.SettingsJson,
            LastTriggeredAt = entity.LastTriggeredAt,
            NextTriggerAt = entity.NextTriggerAt,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };
    }

    private static TriggerEntity MapToEntity(Trigger trigger)
    {
        return new TriggerEntity
        {
            Id = trigger.Id,
            WorkflowDefinitionId = trigger.WorkflowDefinitionId,
            WorkflowVersion = trigger.WorkflowVersion,
            Type = trigger.Type,
            Name = trigger.Name,
            IsActive = trigger.IsActive,
            SettingsJson = trigger.SettingsJson,
            LastTriggeredAt = trigger.LastTriggeredAt,
            NextTriggerAt = trigger.NextTriggerAt,
            CreatedAt = trigger.CreatedAt,
            UpdatedAt = trigger.UpdatedAt,
        };
    }

    private static WebhookRoute MapRouteToDomain(WebhookRouteEntity entity)
    {
        return new WebhookRoute
        {
            Id = entity.Id,
            Path = entity.Path,
            Method = entity.Method,
            WorkflowDefinitionId = entity.WorkflowDefinitionId,
            TriggerId = entity.TriggerId,
            IsStatic = entity.IsStatic,
            Secret = entity.Secret,
            AllowedIpsJson = entity.AllowedIpsJson,
            AllowedOriginsJson = entity.AllowedOriginsJson,
            IsSync = entity.IsSync,
            MaxWaitSeconds = entity.MaxWaitSeconds,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };
    }

    private static WebhookRouteEntity MapRouteToEntity(WebhookRoute route)
    {
        return new WebhookRouteEntity
        {
            Id = route.Id,
            Path = route.Path,
            Method = route.Method,
            WorkflowDefinitionId = route.WorkflowDefinitionId,
            TriggerId = route.TriggerId,
            IsStatic = route.IsStatic,
            Secret = route.Secret,
            AllowedIpsJson = route.AllowedIpsJson,
            AllowedOriginsJson = route.AllowedOriginsJson,
            IsSync = route.IsSync,
            MaxWaitSeconds = route.MaxWaitSeconds,
            CreatedAt = route.CreatedAt,
            UpdatedAt = route.UpdatedAt,
        };
    }
}
