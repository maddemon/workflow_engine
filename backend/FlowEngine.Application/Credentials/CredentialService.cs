using FlowEngine.Application.Audit;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Events;
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
    ICredentialEncryptionService _encryptionService,
    ICryptoKeyProvider _keyProvider,
    IEventBus eventBus,
    AuditEventFactory auditFactory)
{
    private const string KeyVersion = "v1";

    /// <summary>
    /// 创建凭据。
    /// </summary>
    public async Task<CredentialDto> CreateAsync(CreateCredentialDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var key = _keyProvider.GetKey();
        var encryptedData = EncryptFields(dto.Fields, key);

        var credential = new Credential
        {
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

        return MapToDto(credential);
    }

    /// <summary>
    /// 按 ID 获取凭据摘要。
    /// </summary>
    public async Task<CredentialDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var credential = await dbContext.Credentials
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return credential is null ? null : MapToDto(credential);
    }

    /// <summary>
    /// 获取所有凭据摘要列表。
    /// </summary>
    public async Task<IReadOnlyCollection<CredentialDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var credentials = await dbContext.Credentials
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return credentials.Select(MapToDto).ToList();
    }

    /// <summary>
    /// 更新凭据。
    /// </summary>
    public async Task<CredentialDto?> UpdateAsync(Guid id, UpdateCredentialDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var credential = await dbContext.Credentials
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (credential is null)
        {
            return null;
        }

        var key = _keyProvider.GetKey();
        var encryptedData = EncryptFields(dto.Fields, key);

        credential.Name = dto.Name;
        credential.Data = encryptedData;
        credential.KeyVersion = KeyVersion;
        credential.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return MapToDto(credential);
    }

    /// <summary>
    /// 删除凭据。若凭据被工作流引用则返回引用信息。
    /// </summary>
    public async Task<CredentialDeleteResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var credential = await dbContext.Credentials
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (credential is null)
        {
            return new CredentialDeleteResult { NotFound = true };
        }

        var referencingWorkflows = await FindReferencingWorkflowsAsync(id, cancellationToken).ConfigureAwait(false);
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
            result[fieldName] = _encryptionService.Encrypt(value, key);
        }
        return result;
    }

    private async Task<List<string>> FindReferencingWorkflowsAsync(Guid credentialId, CancellationToken cancellationToken)
    {
        var workflows = await dbContext.Workflows
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var referencing = new List<string>();

        foreach (var workflow in workflows)
        {
            if (WorkflowReferencesCredential(workflow, credentialId.ToString()))
            {
                referencing.Add(workflow.Name);
            }
        }

        return referencing;
    }

    private static bool WorkflowReferencesCredential(Workflow workflow, string credentialId)
    {
        foreach (var node in workflow.Nodes)
        {
            foreach (var paramValue in node.Parameters.Values)
            {
                if (paramValue is string strValue && strValue.Equals(credentialId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static CredentialDto MapToDto(Credential credential)
    {
        return new CredentialDto
        {
            Id = credential.Id,
            Name = credential.Name,
            Type = credential.Type,
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
