namespace FlowEngine.Core.Abstractions;

public interface IExecutionIdempotencyService
{
    /// <summary>
    /// 尝试获取或注册幂等键。若 key 已存在且未过期，返回已有 ExecutionId；否则注册新记录并返回 null。
    /// </summary>
    Task<Guid?> TryGetOrRegisterAsync(string idempotencyKey, Guid executionId, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>
    /// 仅查询幂等键是否已存在且未过期，不注册。返回已有 ExecutionId 或 null。
    /// </summary>
    Task<Guid?> TryGetExistingAsync(string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// 清理过期记录。
    /// </summary>
    Task CleanupExpiredAsync(CancellationToken ct = default);
}
