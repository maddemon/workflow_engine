using System.Text.Json;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Infrastructure.Persistence.Repositories;

/// <summary>
/// 凭据仓储实现。
/// </summary>
public sealed class CredentialRepository : ICredentialRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly FlowEngineDbContext _context;

    /// <summary>
    /// 初始化凭据仓储。
    /// </summary>
    /// <param name="context">数据库上下文。</param>
    public CredentialRepository(FlowEngineDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<Credential?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Credentials
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : MapToDomain(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<Credential>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _context.Credentials
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Select(MapToDomain).ToList();
    }

    /// <inheritdoc />
    public async Task SaveAsync(Credential credential, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        var entity = MapToEntity(credential);
        _context.Credentials.Add(entity);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Credentials
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is not null)
        {
            _context.Credentials.Remove(entity);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Credential credential, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        var entity = await _context.Credentials
            .FirstOrDefaultAsync(x => x.Id == credential.Id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return;
        }

        entity.Name = credential.Name;
        entity.Type = credential.Type;
        entity.DataJson = JsonSerializer.Serialize(credential.Data, JsonOptions);
        entity.KeyVersion = credential.KeyVersion;
        entity.UpdatedAt = credential.UpdatedAt;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Credential MapToDomain(CredentialEntity entity)
    {
        var data = string.IsNullOrEmpty(entity.DataJson) || entity.DataJson == "{}"
            ? new Dictionary<string, EncryptedField>()
            : JsonSerializer.Deserialize<Dictionary<string, EncryptedField>>(entity.DataJson, JsonOptions) ?? new Dictionary<string, EncryptedField>();

        return new Credential
        {
            Id = entity.Id,
            Name = entity.Name,
            Type = entity.Type,
            Data = data,
            KeyVersion = entity.KeyVersion,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    private static CredentialEntity MapToEntity(Credential credential)
    {
        return new CredentialEntity
        {
            Id = credential.Id,
            Name = credential.Name,
            Type = credential.Type,
            DataJson = JsonSerializer.Serialize(credential.Data, JsonOptions),
            KeyVersion = credential.KeyVersion,
            CreatedAt = credential.CreatedAt == default ? DateTime.UtcNow : credential.CreatedAt,
            UpdatedAt = credential.UpdatedAt
        };
    }
}
