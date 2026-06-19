using System.Collections.Concurrent;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Runtime.Tests.Executor;

/// <summary>
/// 内存中的执行记录存储，用于执行引擎单元测试。
/// </summary>
public sealed class InMemoryExecutionStore : IExecutionStore
{
    private readonly ConcurrentDictionary<Guid, ExecutionRecord> _records = new();

    /// <inheritdoc />
    public Task<ExecutionRecord?> GetByIdAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        _records.TryGetValue(executionId, out var record);
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<ExecutionRecord>> GetByWorkflowDefinitionIdAsync(
        Guid workflowDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var result = _records.Values
            .Where(r => r.WorkflowDefinitionId == workflowDefinitionId)
            .OrderByDescending(r => r.StartedAt)
            .ToList();

        return Task.FromResult<IReadOnlyCollection<ExecutionRecord>>(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<ExecutionRecord>> GetByStatusAsync(
        ExecutionStatus status,
        CancellationToken cancellationToken = default)
    {
        var result = _records.Values
            .Where(r => r.Status == status)
            .OrderByDescending(r => r.StartedAt)
            .ToList();

        return Task.FromResult<IReadOnlyCollection<ExecutionRecord>>(result);
    }

    /// <inheritdoc />
    public Task SaveAsync(ExecutionRecord executionRecord, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionRecord);
        _records[executionRecord.Id] = executionRecord;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AddNodeRecordAsync(
        Guid executionId,
        NodeExecutionRecord nodeRecord,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodeRecord);

        var record = _records.GetOrAdd(
            executionId,
            _ => new ExecutionRecord
            {
                Id = executionId,
                NodeRecords = []
            });

        if (!record.NodeRecords.Exists(r => r.Id == nodeRecord.Id))
        {
            record.NodeRecords.Add(nodeRecord);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateStatusAsync(Guid executionId, ExecutionStatus status, CancellationToken cancellationToken = default)
    {
        if (_records.TryGetValue(executionId, out var record))
        {
            record.Status = status;
            if (status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
            {
                record.CompletedAt = DateTime.UtcNow;
            }
        }

        return Task.CompletedTask;
    }
}
