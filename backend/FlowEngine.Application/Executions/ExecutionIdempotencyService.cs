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

            // 极端情况：并发插入的记录已过期或被清理，重试插入当前记录。
            // 注意：EF Core 在 SaveChanges 失败后实体仍被追踪为 Added 状态，
            // 直接再次调用 SaveChangesAsync 即可重试，无需再次调用 Add。
            try
            {
                await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                // 最终兜底：再次查询，确保获取到已存在的记录
                var fallback = await dbContext.ExecutionDedups
                    .Where(e => e.IdempotencyKey == idempotencyKey)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);
                return fallback?.ExecutionId;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<Guid?> TryGetExistingAsync(string idempotencyKey, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // AsNoTracking：并发落败者上下文中该幂等行可能已被跟踪为「抢占 claimId」，
        // 若合并本地跟踪的旧值，将无法观察到胜者将其更新为真实执行 id，导致重复执行。
        var existing = await dbContext.ExecutionDedups
            .Where(e => e.IdempotencyKey == idempotencyKey && (e.ExpiresAt == null || e.ExpiresAt > now))
            .AsNoTracking()
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
