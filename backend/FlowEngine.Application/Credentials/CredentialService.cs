using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
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
    IResourceAuthorizationService resourceAuthService,
    IUserContext userContext,
    WorkflowRepository workflowRepository)
{
    private const string KeyVersion = "v1";

    /// <summary>
    /// 创建凭据。
    /// </summary>
    public async Task<CredentialDto> CreateAsync(CreateCredentialDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (!CanWriteCredential())
        {
            throw new PermissionDeniedException("当前用户没有创建凭据的权限。");
        }

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
        var credential = await dbContext.Credentials
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
    public async Task<IReadOnlyCollection<CredentialDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var credentials = await dbContext.Credentials
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var shouldMask = ShouldMaskCredentialValues();
        return credentials.Select(c => MapToDto(c, shouldMask)).ToList();
    }

    /// <summary>
    /// 更新凭据。
    /// </summary>
    public async Task<CredentialDto?> UpdateAsync(Guid id, UpdateCredentialDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (!CanWriteCredential())
        {
            throw new PermissionDeniedException("当前用户没有修改凭据的权限。");
        }

        var credential = await dbContext.Credentials
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (credential is null)
        {
            return null;
        }

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
        if (!IsSystemAdmin())
        {
            throw new PermissionDeniedException("仅管理员可删除凭据。");
        }

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

        await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
            AuditEventTypes.CredentialDeleted,
            "Credential",
            id),
            cancellationToken).ConfigureAwait(false);

        return new CredentialDeleteResult { Deleted = true };
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

        return resourceAuthService.ShouldMaskCredentialValues(userContext.Roles);
    }

    private bool CanWriteCredential()
    {
        return userContext.Roles.Contains(RoleConstants.Admin) || userContext.Roles.Contains(RoleConstants.Editor);
    }

    private bool IsSystemAdmin()
    {
        return userContext.Roles.Contains(RoleConstants.Admin);
    }

    private Dictionary<string, string> DecryptFields(Credential credential)
    {
        if (credential.Data.Count == 0)
        {
            return [];
        }

        var key = keyProvider.GetKey();
        var fields = new Dictionary<string, string>();
        foreach (var (fieldName, encryptedField) in credential.Data)
        {
            fields[fieldName] = encryptionService.DecryptString(encryptedField, key);
        }
        return fields;
    }

    private CredentialDto MapToDto(Credential credential, bool maskValues)
    {
        var fields = DecryptFields(credential);
        if (maskValues)
        {
            foreach (var key in fields.Keys.ToList())
            {
                fields[key] = "***";
            }
        }

        return new CredentialDto
        {
            Id = credential.Id,
            ProjectId = credential.ProjectId,
            Name = credential.Name,
            Type = credential.Type,
            Fields = fields,
            CreatedAt = credential.CreatedAt,
            UpdatedAt = credential.UpdatedAt
        };
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
