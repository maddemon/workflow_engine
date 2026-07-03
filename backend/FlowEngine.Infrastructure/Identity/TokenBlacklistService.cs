using FlowEngine.Application.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace FlowEngine.Infrastructure.Identity;

/// <summary>
/// 基于内存缓存的 Token 黑名单实现。
/// </summary>
public sealed class TokenBlacklistService(IMemoryCache cache) : ITokenBlacklist
{
    private const string KeyPrefix = "token-blacklist:";

    /// <inheritdoc />
    public Task AddAsync(string jti, DateTime expiresAt, CancellationToken cancellationToken = default)
    {
        var key = KeyPrefix + jti;
        var absoluteExpiration = expiresAt > DateTime.UtcNow
            ? expiresAt
            : DateTime.UtcNow.AddHours(1);

        cache.Set(key, true, absoluteExpiration);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> IsBlacklistedAsync(string jti, CancellationToken cancellationToken = default)
    {
        var key = KeyPrefix + jti;
        var blacklisted = cache.TryGetValue(key, out _);
        return Task.FromResult(blacklisted);
    }
}
