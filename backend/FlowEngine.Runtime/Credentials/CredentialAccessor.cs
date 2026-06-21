using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Runtime.Credentials;

/// <summary>
/// 凭据访问器实现。
/// </summary>
public sealed class CredentialAccessor : ICredentialAccessor
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly ICredentialEncryptionService _encryptionService;
    private readonly ICryptoKeyProvider _keyProvider;

    /// <summary>
    /// 初始化凭据访问器。
    /// </summary>
    public CredentialAccessor(
        FlowEngineDbContext dbContext,
        ICredentialEncryptionService encryptionService,
        ICryptoKeyProvider keyProvider)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
    }

    /// <inheritdoc />
    public async Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
    {
        var credential = await _dbContext.Credentials
            .FirstOrDefaultAsync(c => c.Id == credentialId, cancellationToken)
            .ConfigureAwait(false);

        if (credential is null)
        {
            return new CredentialValue
            {
                Name = string.Empty,
                Type = string.Empty,
                Fields = new Dictionary<string, string> { ["__error"] = $"凭据 {credentialId} 不存在" },
                BinaryFields = []
            };
        }

        var key = _keyProvider.GetKey();
        var fields = new Dictionary<string, string>();
        var binaryFields = new Dictionary<string, byte[]>();

        foreach (var (fieldName, encryptedField) in credential.Data)
        {
            if (encryptedField.IsBinary)
            {
                binaryFields[fieldName] = _encryptionService.DecryptBytes(encryptedField, key);
            }
            else
            {
                fields[fieldName] = _encryptionService.DecryptString(encryptedField, key);
            }
        }

        return new CredentialValue
        {
            Name = credential.Name,
            Type = credential.Type,
            Fields = fields,
            BinaryFields = binaryFields
        };
    }
}
