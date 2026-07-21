using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlowEngine.Application.ExecutionCleanup;

/// <summary>
/// 执行清理审计事件。
/// </summary>
public sealed record ExecutionCleanupEvent : AuditEvent;

/// <summary>
/// 执行清理服务，负责清理过期的执行记录。
/// </summary>
public sealed class ExecutionCleanupService(
    FlowEngineDbContext dbContext,
    IOptions<ExecutionCleanupOptions> options,
    IEventBus eventBus,
    IExecutionIdempotencyService idempotencyService,
    ILogger<ExecutionCleanupService> logger)
{
    private readonly ExecutionCleanupOptions _options = options.Value;

    /// <summary>
    /// 执行一次清理：按保留天数清理过期记录，并按工作流裁剪多余记录。
    /// </summary>
    public async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var cutoffDate = DateTime.UtcNow.AddDays(-_options.RetentionDays);
        var terminalStatuses = new[]
        {
            ExecutionStatus.Completed,
            ExecutionStatus.Failed,
            ExecutionStatus.Cancelled,
            ExecutionStatus.Compensated,
            ExecutionStatus.CompensationFailed,
            ExecutionStatus.DryRunCompleted,
        };

        // Phase 1: Delete records older than retention period（分批删除避免超时/锁表）
        var expiredCount = await DeleteExpiredInBatchesAsync(cutoffDate, terminalStatuses, cancellationToken)
            .ConfigureAwait(false);

        if (expiredCount > 0)
        {
            logger.LogInformation("已清理 {Count} 条过期执行记录（完成于 {CutoffDate} 之前）。", expiredCount, cutoffDate);

            await PublishCleanupEventAsync(expiredCount, "retention", cancellationToken).ConfigureAwait(false);
        }

        // Phase 2: For each workflow, keep only MaxRecordsToKeep terminal records（分批删除）
        var workflowIds = await dbContext.ExecutionRecords
            .Select(r => r.WorkflowDefinitionId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var trimmedCount = 0;
        foreach (var workflowId in workflowIds)
        {
            trimmedCount += await TrimWorkflowRecordsInBatchesAsync(workflowId, terminalStatuses, cancellationToken)
                .ConfigureAwait(false);
        }

        if (trimmedCount > 0)
        {
            logger.LogInformation("已为 {WorkflowCount} 个工作流裁剪 {Count} 条多余执行记录。", workflowIds.Count, trimmedCount);

            await PublishCleanupEventAsync(trimmedCount, "max_records", cancellationToken).ConfigureAwait(false);
        }

        if (expiredCount == 0 && trimmedCount == 0)
        {
            logger.LogDebug("执行清理完成，无需清理。");
        }

        // Phase 3: 清理过期的幂等去重记录
        await idempotencyService.CleanupExpiredAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 分批删除过期执行记录，每批 BatchSize 条，批次间短暂延迟减少数据库压力。
    /// </summary>
    private async Task<int> DeleteExpiredInBatchesAsync(
        DateTime cutoffDate,
        ExecutionStatus[] terminalStatuses,
        CancellationToken cancellationToken)
    {
        var batchSize = _options.BatchSize <= 0 ? 500 : _options.BatchSize;
        var batchDelay = _options.BatchDelayMs < 0 ? 0 : _options.BatchDelayMs;
        var totalDeleted = 0;

        while (true)
        {
            var batchIds = await dbContext.ExecutionRecords
                .Where(r => r.CompletedAt != null
                    && r.CompletedAt < cutoffDate
                    && terminalStatuses.Contains(r.Status))
                .OrderBy(r => r.Id)
                .Select(r => r.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (batchIds.Count == 0)
            {
                break;
            }

            totalDeleted += await DeleteBatchByIdsAsync(batchIds, cancellationToken).ConfigureAwait(false);

            if (batchIds.Count < batchSize)
            {
                break;
            }

            if (batchDelay > 0)
            {
                await Task.Delay(batchDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        return totalDeleted;
    }

    /// <summary>
    /// 分批裁剪指定工作流多余的终态记录，每批 BatchSize 条，批次间短暂延迟减少数据库压力。
    /// </summary>
    private async Task<int> TrimWorkflowRecordsInBatchesAsync(
        Guid workflowId,
        ExecutionStatus[] terminalStatuses,
        CancellationToken cancellationToken)
    {
        var batchSize = _options.BatchSize <= 0 ? 500 : _options.BatchSize;
        var batchDelay = _options.BatchDelayMs < 0 ? 0 : _options.BatchDelayMs;
        var totalDeleted = 0;

        while (true)
        {
            var batchIds = await dbContext.ExecutionRecords
                .Where(r => r.WorkflowDefinitionId == workflowId
                    && r.CompletedAt != null
                    && terminalStatuses.Contains(r.Status))
                .OrderByDescending(r => r.CompletedAt)
                .Skip(_options.MaxRecordsToKeep)
                .Take(batchSize)
                .Select(r => r.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (batchIds.Count == 0)
            {
                break;
            }

            totalDeleted += await DeleteBatchByIdsAsync(batchIds, cancellationToken).ConfigureAwait(false);

            if (batchIds.Count < batchSize)
            {
                break;
            }

            if (batchDelay > 0)
            {
                await Task.Delay(batchDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        return totalDeleted;
    }

    /// <summary>
    /// 批量删除指定 ID 的执行记录。
    /// 关系型提供程序使用 <see cref="DbSet{T}.ExecuteDeleteAsync"/> 一次往返删除，避免物化整行实体；
    /// InMemory 提供程序不支持批量删除，退化为按 ID 加载后删除（仅测试路径）。
    /// </summary>
    private async Task<int> DeleteBatchByIdsAsync(
        List<Guid> batchIds, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var deleted = await dbContext.ExecutionRecords
                    .Where(r => batchIds.Contains(r.Id))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return deleted;
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        var batch = await dbContext.ExecutionRecords
            .Where(r => batchIds.Contains(r.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        dbContext.ExecutionRecords.RemoveRange(batch);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return batch.Count;
    }

    private async Task PublishCleanupEventAsync(int recordsDeleted, string reason, CancellationToken cancellationToken)
    {
        var cleanupEvent = new ExecutionCleanupEvent
        {
            EventType = "Execution.Cleanup",
            Actor = "system",
            ResourceType = "ExecutionRecord",
            ResourceId = Guid.Empty,
            Payload = new Dictionary<string, object>
            {
                ["recordsDeleted"] = recordsDeleted,
                ["reason"] = reason,
                ["retentionDays"] = _options.RetentionDays,
                ["maxRecordsToKeep"] = _options.MaxRecordsToKeep,
            },
        };

        await eventBus.PublishAsync(cleanupEvent, cancellationToken).ConfigureAwait(false);
    }
}
