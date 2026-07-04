using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Application.Executions;

/// <summary>
/// 执行幂等服务，基于数据库实现持久化幂等去重。
/// </summary>
public sealed class ExecutionIdempotencyService(
    FlowEngineDbContext dbContext,
    ILogger<ExecutionIdempotencyService> logger) : IExecutionIdempotencyService
{
    /// <inheritdoc />
    public async Task<Guid?> TryGetOrRegisterAsync(
        string idempotencyKey,
        Guid executionId,
        TimeSpan? ttl = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var existing = await dbContext.ExecutionDedups
            .Where(e => e.IdempotencyKey == idempotencyKey && (e.ExpiresAt == null || e.ExpiresAt > now))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            logger.LogDebug("幂等键已存在: Key={Key}, ExecutionId={ExecutionId}", idempotencyKey, existing.ExecutionId);
            return existing.ExecutionId;
        }

        var dedup = new ExecutionDedup
        {
            IdempotencyKey = idempotencyKey,
            ExecutionId = executionId,
            CreatedAt = now,
            ExpiresAt = ttl.HasValue ? now + ttl.Value : null,
        };

        dbContext.ExecutionDedups.Add(dedup);

        try
        {
            await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            logger.LogDebug("幂等键并发冲突，重新查询: Key={Key}", idempotencyKey);

            var concurrent = await dbContext.ExecutionDedups
                .Where(e => e.IdempotencyKey == idempotencyKey && (e.ExpiresAt == null || e.ExpiresAt > now))
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (concurrent is not null)
            {
                return concurrent.ExecutionId;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<Guid?> TryGetExistingAsync(string idempotencyKey, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var existing = await dbContext.ExecutionDedups
            .Where(e => e.IdempotencyKey == idempotencyKey && (e.ExpiresAt == null || e.ExpiresAt > now))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return existing?.ExecutionId;
    }

    /// <inheritdoc />
    public async Task CleanupExpiredAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var deleted = await dbContext.ExecutionDedups
            .Where(e => e.ExpiresAt != null && e.ExpiresAt < now)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        if (deleted > 0)
        {
            logger.LogInformation("已清理 {Count} 条过期幂等去重记录。", deleted);
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner is null)
            return false;

        var message = inner.Message;
        return message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase)
            || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
            || message.Contains("23505", StringComparison.OrdinalIgnoreCase);
    }
}
