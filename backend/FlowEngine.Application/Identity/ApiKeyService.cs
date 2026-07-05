using System.Security.Cryptography;
using System.Text;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Events;
using FlowEngine.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Application.Identity;

/// <summary>
/// API Key 验证结果。
/// </summary>
/// <param name="UserId">用户 ID。</param>
/// <param name="Roles">用户角色列表。</param>
public sealed record ApiKeyValidationResult(Guid UserId, IReadOnlyList<string> Roles);

/// <summary>
/// API Key 应用服务，负责创建、列出、吊销和验证 API Key。
/// </summary>
public class ApiKeyService(
    FlowEngineDbContext dbContext,
    IEventBus eventBus,
    AuditEventFactory auditFactory,
    ILogger<ApiKeyService> logger)
{
    private const string KeyPrefix = "fe_";
    private const int KeyRandomBytes = 32;
    private const int PrefixLength = 8;

    /// <summary>
    /// 创建新的 API Key。
    /// </summary>
    /// <param name="userId">所属用户 ID。</param>
    /// <param name="name">令牌名称。</param>
    /// <param name="expiresAt">过期时间，null 表示永不过期。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>包含明文 Key 的创建结果（明文仅返回一次）。</returns>
    /// <exception cref="ArgumentException">名称为空时抛出。</exception>
    public async Task<CreateApiKeyResult> CreateAsync(
        Guid userId,
        string name,
        DateTime? expiresAt = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("API Key 名称不能为空", nameof(name));
        }

        if (expiresAt.HasValue && expiresAt.Value <= DateTime.UtcNow)
        {
            throw new ArgumentException("过期时间必须晚于当前时间", nameof(expiresAt));
        }

        var plaintextKey = GenerateKey();
        var keyHash = HashKey(plaintextKey);
        var prefix = plaintextKey[..Math.Min(PrefixLength, plaintextKey.Length)];

        var apiKey = new ApiKey
        {
            UserId = userId,
            Name = name.Trim(),
            KeyHash = keyHash,
            Prefix = prefix,
            ExpiresAt = expiresAt,
        };

        dbContext.Set<ApiKey>().Add(apiKey);
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        logger.LogInformation("Created API key {ApiKeyId} for user {UserId}", apiKey.Id, userId);

        await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
            AuditEventTypes.ApiKeyCreated,
            "ApiKey",
            apiKey.Id,
            new Dictionary<string, object> { ["name"] = apiKey.Name }),
            ct).ConfigureAwait(false);

        return new CreateApiKeyResult
        {
            Id = apiKey.Id,
            Name = apiKey.Name,
            Prefix = apiKey.Prefix,
            ExpiresAt = apiKey.ExpiresAt,
            Key = plaintextKey,
        };
    }

    /// <summary>
    /// 列出指定用户的所有 API Key（不包含明文）。
    /// </summary>
    /// <param name="userId">用户 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>API Key 列表。</returns>
    public async Task<IReadOnlyList<ApiKeyDto>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var apiKeys = await dbContext.Set<ApiKey>()
            .AsNoTracking()
            .Where(x => x.UserId == userId && !x.Deleted)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return apiKeys.Select(x => new ApiKeyDto
        {
            Id = x.Id,
            Name = x.Name,
            Prefix = x.Prefix,
            CreatedAt = x.CreatedAt,
            ExpiresAt = x.ExpiresAt,
            RevokedAt = x.RevokedAt,
        }).ToList();
    }

    /// <summary>
    /// 吊销指定 API Key。
    /// </summary>
    /// <param name="userId">当前用户 ID。</param>
    /// <param name="keyId">API Key ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>是否成功吊销。</returns>
    public async Task<bool> RevokeAsync(Guid userId, Guid keyId, CancellationToken ct = default)
    {
        var apiKey = await dbContext.Set<ApiKey>()
            .FirstOrDefaultAsync(x => x.Id == keyId && x.UserId == userId && !x.Deleted && x.RevokedAt == null, ct)
            .ConfigureAwait(false);

        if (apiKey is null)
        {
            return false;
        }

        apiKey.RevokedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        logger.LogInformation("Revoked API key {ApiKeyId} for user {UserId}", apiKey.Id, userId);

        await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
            AuditEventTypes.ApiKeyRevoked,
            "ApiKey",
            apiKey.Id,
            new Dictionary<string, object> { ["name"] = apiKey.Name }),
            ct).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// 验证 API Key 是否有效。
    /// </summary>
    /// <param name="key">Key 明文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>有效时返回用户 ID 与角色列表，否则返回 null。</returns>
    public async Task<ApiKeyValidationResult?> ValidateAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var keyHash = HashKey(key);
        var apiKey = await dbContext.Set<ApiKey>()
            .AsNoTracking()
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.KeyHash == keyHash && !x.Deleted && x.RevokedAt == null, ct)
            .ConfigureAwait(false);

        if (apiKey is null)
        {
            return null;
        }

        if (apiKey.ExpiresAt.HasValue && apiKey.ExpiresAt.Value <= DateTime.UtcNow)
        {
            return null;
        }

        if (!apiKey.User.IsActive)
        {
            return null;
        }

        var roles = await dbContext.Set<UserRole>()
            .AsNoTracking()
            .Where(x => x.UserId == apiKey.UserId && !x.Deleted)
            .Select(x => x.Role)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new ApiKeyValidationResult(apiKey.UserId, roles);
    }

    private static string GenerateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(KeyRandomBytes);
        var base64 = Convert.ToBase64String(bytes);
        var token = base64
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .Replace("=", string.Empty, StringComparison.Ordinal);
        return KeyPrefix + token;
    }

    private static string HashKey(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
