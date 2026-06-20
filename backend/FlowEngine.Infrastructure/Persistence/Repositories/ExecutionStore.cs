using System.Text.Json;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Infrastructure.Persistence.Repositories;

/// <summary>
/// 执行记录存储实现。
/// </summary>
public sealed class ExecutionStore : IExecutionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly FlowEngineDbContext _context;

    /// <summary>
    /// 初始化执行记录存储。
    /// </summary>
    /// <param name="context">数据库上下文。</param>
    public ExecutionStore(FlowEngineDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<ExecutionRecord?> GetByIdAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.ExecutionRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == executionId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        var nodeRecordEntities = await _context.NodeExecutionRecords
            .AsNoTracking()
            .Where(x => x.ExecutionId == executionId)
            .OrderBy(x => x.RunIndex)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return MapToDomain(entity, nodeRecordEntities);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<ExecutionRecord>> GetByWorkflowDefinitionIdAsync(
        Guid workflowDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _context.ExecutionRecords
            .AsNoTracking()
            .Where(x => x.WorkflowDefinitionId == workflowDefinitionId)
            .OrderByDescending(x => x.StartedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var results = new List<ExecutionRecord>();
        foreach (var entity in entities)
        {
            var nodeRecordEntities = await _context.NodeExecutionRecords
                .AsNoTracking()
                .Where(x => x.ExecutionId == entity.Id)
                .OrderBy(x => x.RunIndex)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            results.Add(MapToDomain(entity, nodeRecordEntities));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<ExecutionRecord>> GetByStatusAsync(
        ExecutionStatus status,
        CancellationToken cancellationToken = default)
    {
        var entities = await _context.ExecutionRecords
            .AsNoTracking()
            .Where(x => x.Status == status)
            .OrderByDescending(x => x.StartedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var results = new List<ExecutionRecord>();
        foreach (var entity in entities)
        {
            var nodeRecordEntities = await _context.NodeExecutionRecords
                .AsNoTracking()
                .Where(x => x.ExecutionId == entity.Id)
                .OrderBy(x => x.RunIndex)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            results.Add(MapToDomain(entity, nodeRecordEntities));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task SaveAsync(ExecutionRecord executionRecord, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionRecord);

        var existing = await _context.ExecutionRecords
            .FirstOrDefaultAsync(x => x.Id == executionRecord.Id, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            MapToExistingEntity(executionRecord, existing);
        }
        else
        {
            _context.ExecutionRecords.Add(MapToEntity(executionRecord));
        }

        var changes = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddNodeRecordAsync(
        Guid executionId,
        NodeExecutionRecord nodeRecord,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodeRecord);

        var entity = MapToEntity(nodeRecord, executionId);
        _context.NodeExecutionRecords.Add(entity);

        var changes = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateStatusAsync(
        Guid executionId,
        ExecutionStatus status,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.ExecutionRecords
            .FirstOrDefaultAsync(x => x.Id == executionId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return;
        }

        entity.Status = status;
        if (status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
        {
            entity.CompletedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ExecutionRecord MapToDomain(ExecutionRecordEntity entity, List<NodeExecutionRecordEntity> nodeRecordEntities)
    {
        var nodeRecords = nodeRecordEntities.Select(ne => new NodeExecutionRecord
        {
            Id = ne.Id,
            NodeDefinitionId = ne.NodeDefinitionId,
            RunIndex = ne.RunIndex,
            StartedAt = ne.StartedAt,
            CompletedAt = ne.CompletedAt,
            Inputs = DeserializeDict<String, DataBatch>(ne.InputsJson) ?? new Dictionary<string, DataBatch>(),
            Output = DeserializeObj<NodeExecutionResult>(ne.OutputJson) ?? new NodeExecutionResult(),
            RawParameters = DeserializeDict<String, object>(ne.RawParametersJson) ?? new Dictionary<string, object>(),
            ResolvedParameters = DeserializeDict<String, object>(ne.ResolvedParametersJson) ?? new Dictionary<string, object>(),
        }).ToList();

        return new ExecutionRecord
        {
            Id = entity.Id,
            WorkflowDefinitionId = entity.WorkflowDefinitionId,
            ParentExecutionId = entity.ParentExecutionId,
            StartedAt = entity.StartedAt,
            CompletedAt = entity.CompletedAt,
            Status = entity.Status,
            NodeRecords = nodeRecords
        };
    }

    private static Dictionary<TKey, TValue>? DeserializeDict<TKey, TValue>(string? json)
        where TKey : notnull
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<Dictionary<TKey, TValue>>(json, JsonOptions);
    }

    private static T? DeserializeObj<T>(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static ExecutionRecordEntity MapToEntity(ExecutionRecord record)
    {
        return new ExecutionRecordEntity
        {
            Id = record.Id,
            WorkflowDefinitionId = record.WorkflowDefinitionId,
            ParentExecutionId = record.ParentExecutionId,
            StartedAt = record.StartedAt == default ? DateTime.UtcNow : record.StartedAt,
            CompletedAt = record.CompletedAt,
            Status = record.Status,
            NodeRecordsJson = JsonSerializer.Serialize(record.NodeRecords, JsonOptions)
        };
    }

    private static void MapToExistingEntity(ExecutionRecord record, ExecutionRecordEntity entity)
    {
        entity.WorkflowDefinitionId = record.WorkflowDefinitionId;
        entity.ParentExecutionId = record.ParentExecutionId;
        entity.StartedAt = record.StartedAt == default ? DateTime.UtcNow : record.StartedAt;
        entity.CompletedAt = record.CompletedAt;
        entity.Status = record.Status;
        entity.NodeRecordsJson = JsonSerializer.Serialize(record.NodeRecords, JsonOptions);
    }

    private static NodeExecutionRecordEntity MapToEntity(NodeExecutionRecord record, Guid executionId)
    {
        return new NodeExecutionRecordEntity
        {
            Id = record.Id,
            ExecutionId = executionId,
            NodeDefinitionId = record.NodeDefinitionId,
            RunIndex = record.RunIndex,
            StartedAt = record.StartedAt == default ? DateTime.UtcNow : record.StartedAt,
            CompletedAt = record.CompletedAt,
            InputsJson = JsonSerializer.Serialize(record.Inputs, JsonOptions),
            OutputJson = JsonSerializer.Serialize(record.Output, JsonOptions),
            RawParametersJson = JsonSerializer.Serialize(record.RawParameters, JsonOptions),
            ResolvedParametersJson = JsonSerializer.Serialize(record.ResolvedParameters, JsonOptions)
        };
    }
}
