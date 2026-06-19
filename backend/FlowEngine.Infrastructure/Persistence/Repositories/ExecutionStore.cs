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
            .Include(x => x.NodeExecutions)
            .FirstOrDefaultAsync(x => x.Id == executionId, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : MapToDomain(entity);
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

        return entities.Select(MapToDomain).ToList();
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

        return entities.Select(MapToDomain).ToList();
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

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

    private static ExecutionRecord MapToDomain(ExecutionRecordEntity entity)
    {
        var nodeRecords = string.IsNullOrEmpty(entity.NodeRecordsJson)
            ? new List<NodeExecutionRecord>()
            : JsonSerializer.Deserialize<List<NodeExecutionRecord>>(entity.NodeRecordsJson, JsonOptions) ?? new List<NodeExecutionRecord>();

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
