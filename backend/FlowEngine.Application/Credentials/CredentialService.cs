using FlowEngine.Application.Audit;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Events;

namespace FlowEngine.Application.Credentials;

/// <summary>
/// 凭据应用服务，编排凭据 CRUD 与加密。
/// </summary>
public sealed class CredentialService
{
    private const string KeyVersion = "v1";

    private readonly ICredentialRepository _credentialRepository;
    private readonly IWorkflowRepository _workflowRepository;
    private readonly ICredentialEncryptionService _encryptionService;
    private readonly ICryptoKeyProvider _keyProvider;
    private readonly IEventBus _eventBus;
    private readonly AuditEventFactory _auditFactory;

    /// <summary>
    /// 初始化凭据应用服务。
    /// </summary>
    public CredentialService(
        ICredentialRepository credentialRepository,
        IWorkflowRepository workflowRepository,
        ICredentialEncryptionService encryptionService,
        ICryptoKeyProvider keyProvider,
        IEventBus eventBus,
        AuditEventFactory auditFactory)
    {
        _credentialRepository = credentialRepository ?? throw new ArgumentNullException(nameof(credentialRepository));
        _workflowRepository = workflowRepository ?? throw new ArgumentNullException(nameof(workflowRepository));
        _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        _eventBus = eventBus;
        _auditFactory = auditFactory;
    }

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

        await _credentialRepository.SaveAsync(credential, cancellationToken).ConfigureAwait(false);

        await _eventBus.PublishAsync(_auditFactory.Create<AuditLogEvent>(
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
        var credential = await _credentialRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return credential is null ? null : MapToDto(credential);
    }

    /// <summary>
    /// 获取所有凭据摘要列表。
    /// </summary>
    public async Task<IReadOnlyCollection<CredentialDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var credentials = await _credentialRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return credentials.Select(MapToDto).ToList();
    }

    /// <summary>
    /// 更新凭据。
    /// </summary>
    public async Task<CredentialDto?> UpdateAsync(Guid id, UpdateCredentialDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var credential = await _credentialRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
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

        await _credentialRepository.UpdateAsync(credential, cancellationToken).ConfigureAwait(false);

        return MapToDto(credential);
    }

    /// <summary>
    /// 删除凭据。若凭据被工作流引用则返回引用信息。
    /// </summary>
    public async Task<CredentialDeleteResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var credential = await _credentialRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (credential is null)
        {
            return new CredentialDeleteResult { NotFound = true };
        }

        var referencingWorkflows = await FindReferencingWorkflowsAsync(id, cancellationToken).ConfigureAwait(false);
        if (referencingWorkflows.Count > 0)
        {
            return new CredentialDeleteResult { ReferencedBy = referencingWorkflows };
        }

        await _credentialRepository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

        await _eventBus.PublishAsync(_auditFactory.Create<AuditLogEvent>(
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
        var credentialIdStr = credentialId.ToString();
        var workflows = await _workflowRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var referencing = new List<string>();

        foreach (var workflow in workflows)
        {
            if (WorkflowReferencesCredential(workflow, credentialIdStr))
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
