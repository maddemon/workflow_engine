using System.Text.Json;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Infrastructure.Persistence.Repositories;

/// <summary>
/// 工作流仓储实现。
/// </summary>
public sealed class WorkflowRepository : IWorkflowRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly FlowEngineDbContext _context;

    /// <summary>
    /// 初始化工作流仓储。
    /// </summary>
    /// <param name="context">数据库上下文。</param>
    public WorkflowRepository(FlowEngineDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<Workflow?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.WorkflowDefinitions
            .Where(x => x.Id == id)
            .AsNoTracking()
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : MapToDomain(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<Workflow>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _context.WorkflowDefinitions
            .GroupBy(x => x.Id)
            .Select(g => g.OrderByDescending(x => x.Version).First())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(MapToDomain).ToList();
    }

    /// <inheritdoc />
    public async Task SaveAsync(Workflow workflow, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        var latestVersion = await _context.WorkflowDefinitions
            .Where(x => x.Id == workflow.Id)
            .Select(x => (int?)x.Version)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);

        var nextVersion = latestVersion.GetValueOrDefault() + 1;

        var entity = MapToEntity(workflow, nextVersion);
        _context.WorkflowDefinitions.Add(entity);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entities = await _context.WorkflowDefinitions
            .Where(x => x.Id == id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        _context.WorkflowDefinitions.RemoveRange(entities);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Workflow?> GetByVersionAsync(Guid id, int version, CancellationToken cancellationToken = default)
    {
        var entity = await _context.WorkflowDefinitions
            .FirstOrDefaultAsync(x => x.Id == id && x.Version == version, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : MapToDomain(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<int>> GetVersionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var versions = await _context.WorkflowDefinitions
            .Where(x => x.Id == id)
            .OrderBy(x => x.Version)
            .Select(x => x.Version)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return versions;
    }

    private static Workflow MapToDomain(WorkflowDefinitionEntity entity)
    {
        var nodes = string.IsNullOrEmpty(entity.NodesJson)
            ? new List<NodeInstance>()
            : JsonSerializer.Deserialize<List<NodeInstance>>(entity.NodesJson, JsonOptions) ?? new List<NodeInstance>();

        var connections = string.IsNullOrEmpty(entity.ConnectionsJson)
            ? new List<Connection>()
            : JsonSerializer.Deserialize<List<Connection>>(entity.ConnectionsJson, JsonOptions) ?? new List<Connection>();

        var styleSettings = string.IsNullOrEmpty(entity.StyleSettingsJson)
            ? null
            : JsonSerializer.Deserialize<WorkflowStyleSettings>(entity.StyleSettingsJson, JsonOptions);

        return new Workflow
        {
            Id = entity.Id,
            ProjectId = entity.ProjectId,
            Name = entity.Name,
            Version = entity.Version,
            CreatedBy = entity.CreatedBy,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            IsActive = entity.IsActive,
            StyleSettings = styleSettings,
            Nodes = nodes,
            Connections = connections
        };
    }

    private static WorkflowDefinitionEntity MapToEntity(Workflow workflow, int version)
    {
        return new WorkflowDefinitionEntity
        {
            Id = workflow.Id,
            ProjectId = workflow.ProjectId,
            Name = workflow.Name,
            Version = version,
            CreatedBy = workflow.CreatedBy,
            CreatedAt = workflow.CreatedAt == default ? DateTime.UtcNow : workflow.CreatedAt,
            UpdatedAt = workflow.UpdatedAt == default ? DateTime.UtcNow : workflow.UpdatedAt,
            IsActive = workflow.IsActive,
            StyleSettingsJson = workflow.StyleSettings is null ? null : JsonSerializer.Serialize(workflow.StyleSettings, JsonOptions),
            NodesJson = JsonSerializer.Serialize(workflow.Nodes, JsonOptions),
            ConnectionsJson = JsonSerializer.Serialize(workflow.Connections, JsonOptions)
        };
    }
}
