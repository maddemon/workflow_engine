using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using Mapster;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Credentials;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Credentials;

/// <summary>
/// 凭据应用服务，编排凭据 CRUD 与加密。
/// </summary>
/// <remarks>
/// 初始化凭据应用服务。
/// </remarks>
public sealed class CredentialService(
    FlowEngineDbContext dbContext,
    ICredentialEncryptionService encryptionService,
    ICryptoKeyProvider keyProvider,
    IEventBus eventBus,
    AuditEventFactory auditFactory,
    IResourceAuthorizationService resourceAuthorization,
    IUserContext userContext,
    WorkflowRepository workflowRepository,
    IAuthorizationGuard authGuard,
    CredentialTypeRegistry credentialTypeRegistry,
    AuthorizedOperationHandler handler)
{
    private const string KeyVersion = "v1";

    private static readonly AuthorizationPolicy UpdatePolicy = new(
        ResourceKind.Credential, Operation.Write, Scope.Credential, AdminPhase: false, ProjectScoped: false);
    private static readonly AuthorizationPolicy DeletePolicy = new(
        ResourceKind.Credential, Operation.Delete, Scope: null, AdminPhase: true, ProjectScoped: false);

    /// <summary>
    /// 创建凭据。
    /// </summary>
    public async Task<CredentialDto> CreateAsync(CreateCredentialDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        await authGuard.RequireScopeAsync(Scope.Credential, Operation.Write, cancellationToken);

        ValidateCredentialType(dto.Type, dto.Fields);
        await ValidateNameNotInUseAsync(dto.Name, dto.ProjectId, null, cancellationToken).ConfigureAwait(false);

        return await CreateCredentialInternalAsync(dto, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 幂等创建或更新凭据。按 (Name, Type, ProjectId) 查找，存在则覆盖 Fields，不存在则创建。
    /// </summary>
    public async Task<(CredentialDto Credential, bool Created)> EnsureAsync(CreateCredentialDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        await authGuard.RequireScopeAsync(Scope.Credential, Operation.Write, cancellationToken);

        ValidateCredentialType(dto.Type, dto.Fields);

        var existing = await dbContext.Credentials
            .FirstOrDefaultAsync(
                c => c.Name == dto.Name
                     && c.Type == dto.Type
                     && c.ProjectId == dto.ProjectId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            await authGuard.RequireAccessAsync(ResourceKind.Credential, existing.Id, Operation.Write, cancellationToken);

            var key = keyProvider.GetKey();
            existing.Data = EncryptFields(dto.Fields, key);
            existing.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                AuditEventTypes.CredentialUpdated,
                "Credential",
                existing.Id,
                new Dictionary<string, object> { ["name"] = existing.Name, ["type"] = existing.Type }),
                cancellationToken).ConfigureAwait(false);

            return (MapToDto(existing, maskValues: false), false);
        }

        await ValidateNameNotInUseAsync(dto.Name, dto.ProjectId, null, cancellationToken).ConfigureAwait(false);
        var created = await CreateCredentialInternalAsync(dto, cancellationToken).ConfigureAwait(false);
        return (created, true);
    }

    private async Task<CredentialDto> CreateCredentialInternalAsync(CreateCredentialDto dto, CancellationToken cancellationToken)
    {
        var key = keyProvider.GetKey();
        var encryptedData = EncryptFields(dto.Fields, key);

        var credential = new Credential
        {
            ProjectId = dto.ProjectId,
            Name = dto.Name,
            Type = dto.Type,
            Data = encryptedData,
            KeyVersion = KeyVersion,
        };

        dbContext.Credentials.Add(credential);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
            AuditEventTypes.CredentialCreated,
            "Credential",
            credential.Id,
            new Dictionary<string, object> { ["name"] = credential.Name, ["type"] = credential.Type }),
            cancellationToken).ConfigureAwait(false);

        return MapToDto(credential, maskValues: false);
    }

    /// <summary>
    /// 按 ID 获取凭据摘要。
    /// </summary>
    public async Task<CredentialDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await authGuard.RequireAccessAsync(ResourceKind.Credential, id, Operation.Read, cancellationToken);

        var credential = await dbContext.Credentials
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (credential is null)
        {
            return null;
        }

        var shouldMask = ShouldMaskCredentialValues();
        return MapToDto(credential, shouldMask);
    }

    /// <summary>
    /// 获取所有凭据摘要列表。项目（ProjectId）仅作为分类字段，不做隔离。
    /// </summary>
    public async Task<IReadOnlyCollection<CredentialDto>> GetAllAsync(
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Credentials.AsNoTracking();
        if (projectId.HasValue)
        {
            query = query.Where(c => c.ProjectId == projectId.Value);
        }

        var credentials = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        var shouldMask = ShouldMaskCredentialValues();
        return credentials.Select(c => MapToDto(c, shouldMask)).ToList();
    }

    /// <summary>
    /// 更新凭据。
    /// </summary>
    public async Task<CredentialDto?> UpdateAsync(Guid id, UpdateCredentialDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        await handler.AuthorizePreAsync(UpdatePolicy, id, cancellationToken);

        var credential = await dbContext.Credentials
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (credential is null)
        {
            return null;
        }

        ValidateCredentialType(credential.Type, dto.Fields);
        await ValidateNameNotInUseAsync(dto.Name, credential.ProjectId, credential.Id, cancellationToken).ConfigureAwait(false);

        var key = keyProvider.GetKey();
        var encryptedData = EncryptFields(dto.Fields, key);

        credential.Name = dto.Name;
        credential.Data = encryptedData;
        credential.KeyVersion = KeyVersion;
        credential.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return MapToDto(credential, maskValues: false);
    }

    /// <summary>
    /// 删除凭据。若凭据被工作流引用则返回引用信息。
    /// </summary>
    public async Task<CredentialDeleteResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await handler.AuthorizePreAsync(DeletePolicy, id, cancellationToken);

        var credential = await dbContext.Credentials
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (credential is null)
        {
            return new CredentialDeleteResult { NotFound = true };
        }

        var referencingWorkflows = await workflowRepository.FindReferencingCredentialAsync(id, cancellationToken).ConfigureAwait(false);
        if (referencingWorkflows.Count > 0)
        {
            return new CredentialDeleteResult { ReferencedBy = referencingWorkflows };
        }

        dbContext.Credentials.Remove(credential);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await handler.PublishAuditAsync(
            AuditEventTypes.CredentialDeleted,
            "Credential",
            id,
            ct: cancellationToken).ConfigureAwait(false);

        return new CredentialDeleteResult { Deleted = true };
    }

    private void ValidateCredentialType(string type, Dictionary<string, string> fields)
    {
        var validationResult = credentialTypeRegistry.Validate(type, fields);
        if (!validationResult.IsValid)
        {
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException(validationResult.ErrorMessage);
        }
    }

    private async Task ValidateNameNotInUseAsync(string name, Guid? projectId, Guid? excludeId, CancellationToken cancellationToken)
    {
        var exists = await dbContext.Credentials
            .AsNoTracking()
            .AnyAsync(
                c => c.Name == name
                     && c.ProjectId == projectId
                     && (!excludeId.HasValue || c.Id != excludeId.Value),
                cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            // TODO(i18n): 将 BusinessException 消息改为注入 IStringLocalizer 后本地化
            throw new BusinessException($"A credential with the name '{name}' already exists in the project.");
        }
    }

    private Dictionary<string, EncryptedField> EncryptFields(Dictionary<string, string> fields, byte[] key)
    {
        var result = new Dictionary<string, EncryptedField>();
        foreach (var (fieldName, value) in fields)
        {
            result[fieldName] = encryptionService.Encrypt(value, key);
        }
        return result;
    }

    private bool ShouldMaskCredentialValues()
    {
        if (!userContext.IsAuthenticated || userContext.Roles.Count == 0)
        {
            return false;
        }

        return resourceAuthorization.ShouldMaskCredentialValues(userContext.Roles);
    }

    private Dictionary<string, string> DecryptFields(Credential credential)
    {
        if (credential.Data.Count == 0)
        {
            return [];
        }

        var key = keyProvider.GetKey(credential.KeyVersion);
        var fields = new Dictionary<string, string>();
        foreach (var (fieldName, encryptedField) in credential.Data)
        {
            fields[fieldName] = encryptionService.DecryptString(encryptedField, key);
        }
        return fields;
    }

    private CredentialDto MapToDto(Credential credential, bool maskValues)
    {
        Dictionary<string, string> fields;
        
        if (maskValues)
        {
            // 脱敏场景：直接返回占位符，无需解密
            fields = credential.Data.ToDictionary(
                kv => kv.Key, 
                _ => "***");
        }
        else
        {
            // 非脱敏场景：执行解密
            fields = DecryptFields(credential);
        }

        return credential.Adapt<CredentialDto>() with { Fields = fields };
    }
}

/// <summary>
/// 凭据删除结果。
/// </summary>
public sealed class CredentialDeleteResult
{
    /// <summary>
    /// 是否已删除。
    /// </summary>
    public bool Deleted { get; init; }

    /// <summary>
    /// 是否未找到。
    /// </summary>
    public bool NotFound { get; init; }

    /// <summary>
    /// 引用该凭据的工作流名称列表。
    /// </summary>
    public List<string> ReferencedBy { get; init; } = [];
}
