using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Credentials;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Credentials;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using FlowEngine.Application.Tests.TestSupport.Fakes;
using FlowEngine.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace FlowEngine.Application.Tests.Credentials;

public sealed class CredentialServiceTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly RecordingEventBus _eventBus;
    private readonly CredentialService _service;
    private readonly StubEncryptionService _encryptionService;
    private readonly StubKeyProvider _keyProvider;
    private readonly FakeUserContext _userContext;

    public CredentialServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _eventBus = new RecordingEventBus();
        _encryptionService = new StubEncryptionService();
        _keyProvider = new StubKeyProvider();
        _userContext = new FakeUserContext();
        _userContext.Roles = [RoleConstants.Admin];
        var auditFactory = new AuditEventFactory(_userContext);
        var resourceAuthService = new StubResourceAuthorizationService();
        var authGuard = AuthorizationGuardFactory.Create(_userContext, resourceAuthService);
        var handler = new AuthorizedOperationHandler(authGuard, _eventBus, auditFactory);
        _service = new CredentialService(
            _dbContext,
            _encryptionService,
            _keyProvider,
            _eventBus,
            auditFactory,
            resourceAuthService,
            _userContext,
            new WorkflowRepository(_dbContext),
            authGuard,
            new CredentialTypeRegistry(),
            handler);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task CreateAsync_ValidDto_ReturnsDto()
    {
        var ct = TestContext.Current.CancellationToken;
        var dto = new CreateCredentialDto
        {
            Name = "Test API Key",
            Type = "apiKey",
            Fields = new Dictionary<string, string> { ["apiKey"] = "sk-123456" },
        };

        var result = await _service.CreateAsync(dto, ct);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Test API Key", result.Name);
        Assert.Equal("apiKey", result.Type);
    }

    [Fact]
    public async Task CreateAsync_EncryptsFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var dto = new CreateCredentialDto
        {
            Name = "Test",
            Type = "apiKey",
            Fields = new Dictionary<string, string> { ["apiKey"] = "plaintext-value" },
        };

        await _service.CreateAsync(dto, ct);

        var credential = await _dbContext.Credentials.FirstAsync(ct);
        Assert.True(credential.Data.ContainsKey("apiKey"));
        Assert.Equal("encrypted:plaintext-value", credential.Data["apiKey"].CipherText);
    }

    [Fact]
    public async Task CreateAsync_NullDto_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.CreateAsync(null!, ct));
    }

    [Fact]
    public async Task CreateAsync_PublishesAuditEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var dto = new CreateCredentialDto
        {
            Name = "Test",
            Type = "apiKey",
            Fields = new Dictionary<string, string> { ["apiKey"] = "sk-test" },
        };

        await _service.CreateAsync(dto, ct);

        Assert.True(_eventBus.PublishedEvents.Count > 0);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingCredential_ReturnsDto()
    {
        var ct = TestContext.Current.CancellationToken;
        var credential = CreateTestCredential();
        _dbContext.Credentials.Add(credential);
        await _dbContext.SaveChangesAsync(ct);

        var result = await _service.GetAsync(credential.Id, ct);

        Assert.NotNull(result);
        Assert.Equal(credential.Id, result.Id);
        Assert.Equal(credential.Name, result.Name);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingCredential_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _service.GetAsync(Guid.NewGuid(), ct);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        _dbContext.Credentials.AddRange(
            CreateTestCredential("Key 1", projectId: projectId),
            CreateTestCredential("Key 2", projectId: projectId));
        await _dbContext.SaveChangesAsync(ct);

        var results = await _service.GetAllAsync(cancellationToken: ct);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task UpdateAsync_ExistingCredential_UpdatesFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var credential = CreateTestCredential("Original");
        _dbContext.Credentials.Add(credential);
        await _dbContext.SaveChangesAsync(ct);

        var dto = new UpdateCredentialDto
        {
            Name = "Updated",
            Fields = new Dictionary<string, string> { ["apiKey"] = "new-value" },
        };

        var result = await _service.UpdateAsync(credential.Id, dto, ct);

        Assert.NotNull(result);
        Assert.Equal("Updated", result.Name);

        var updated = await _dbContext.Credentials.FindAsync([credential.Id], ct);
        Assert.NotNull(updated);
        Assert.Equal("encrypted:new-value", updated.Data["apiKey"].CipherText);
    }

    [Fact]
    public async Task UpdateAsync_NonExistingCredential_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var dto = new UpdateCredentialDto { Name = "Test", Fields = new Dictionary<string, string> { ["key"] = "value" } };
        var result = await _service.UpdateAsync(Guid.NewGuid(), dto, ct);
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_MissingRequiredFields_ThrowsBusinessException()
    {
        var ct = TestContext.Current.CancellationToken;
        var credential = CreateTestCredential("Original", type: "oauth2");
        credential.Data = new Dictionary<string, EncryptedField>
        {
            ["tokenUrl"] = _encryptionService.Encrypt("https://example.com/token", _keyProvider.GetKey()),
            ["clientId"] = _encryptionService.Encrypt("client-id", _keyProvider.GetKey()),
            ["clientSecret"] = _encryptionService.Encrypt("client-secret", _keyProvider.GetKey()),
        };
        _dbContext.Credentials.Add(credential);
        await _dbContext.SaveChangesAsync(ct);

        var dto = new UpdateCredentialDto
        {
            Name = "Updated",
            Fields = new Dictionary<string, string> { ["tokenUrl"] = "https://example.com/token" },
        };

        var exception = await Assert.ThrowsAsync<BusinessException>(() => _service.UpdateAsync(credential.Id, dto, ct));
        Assert.Contains("缺少必填字段", exception.Message);
        Assert.Contains("clientId", exception.Message);
        Assert.Contains("clientSecret", exception.Message);
    }

    [Fact]
    public async Task UpdateAsync_InvalidOAuth2Provider_ThrowsBusinessException()
    {
        var ct = TestContext.Current.CancellationToken;
        var credential = CreateTestCredential("Original", type: "oauth2");
        credential.Data = new Dictionary<string, EncryptedField>
        {
            ["tokenUrl"] = _encryptionService.Encrypt("https://example.com/token", _keyProvider.GetKey()),
            ["clientId"] = _encryptionService.Encrypt("client-id", _keyProvider.GetKey()),
            ["clientSecret"] = _encryptionService.Encrypt("client-secret", _keyProvider.GetKey()),
        };
        _dbContext.Credentials.Add(credential);
        await _dbContext.SaveChangesAsync(ct);

        var dto = new UpdateCredentialDto
        {
            Name = "Updated",
            Fields = new Dictionary<string, string>
            {
                ["tokenUrl"] = "https://example.com/token",
                ["clientId"] = "client-id",
                ["clientSecret"] = "client-secret",
                ["provider"] = "dingtalk-wrong"
            },
        };

        var exception = await Assert.ThrowsAsync<BusinessException>(() => _service.UpdateAsync(credential.Id, dto, ct));
        Assert.Contains("provider", exception.Message);
        Assert.Contains("dingtalk-wrong", exception.Message);
    }

    [Fact]
    public async Task UpdateAsync_NullDto_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.UpdateAsync(Guid.NewGuid(), null!, ct));
    }

    [Fact]
    public async Task DeleteAsync_ExistingUnreferencedCredential_Deletes()
    {
        var ct = TestContext.Current.CancellationToken;
        var credential = CreateTestCredential();
        _dbContext.Credentials.Add(credential);
        await _dbContext.SaveChangesAsync(ct);

        var result = await _service.DeleteAsync(credential.Id, ct);

        Assert.True(result.Deleted);
        Assert.False(result.NotFound);
        Assert.Empty(result.ReferencedBy);

        var deleted = await _dbContext.Credentials.FindAsync([credential.Id], ct);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task DeleteAsync_NonExistingCredential_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _service.DeleteAsync(Guid.NewGuid(), ct);
        Assert.True(result.NotFound);
        Assert.False(result.Deleted);
    }

    [Fact]
    public async Task EnsureAsync_NewCredential_CreatesAndReturnsCreated()
    {
        var ct = TestContext.Current.CancellationToken;
        var dto = new CreateCredentialDto
        {
            Name = "Ensure Key",
            Type = "apiKey",
            Fields = new Dictionary<string, string> { ["apiKey"] = "sk-ensure" },
        };

        var (result, created) = await _service.EnsureAsync(dto, ct);

        Assert.True(created);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Ensure Key", result.Name);
        Assert.Equal("apiKey", result.Type);
        Assert.Equal("sk-ensure", result.Fields["apiKey"]);
    }

    [Fact]
    public async Task EnsureAsync_ExistingCredential_UpdatesFieldsAndReturnsNotCreated()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var existing = CreateTestCredential("Ensure Existing", projectId: projectId);
        existing.Data = new Dictionary<string, EncryptedField>
        {
            ["apiKey"] = _encryptionService.Encrypt("old-value", _keyProvider.GetKey()),
        };
        _dbContext.Credentials.Add(existing);
        await _dbContext.SaveChangesAsync(ct);

        var dto = new CreateCredentialDto
        {
            Name = existing.Name,
            Type = existing.Type,
            ProjectId = projectId,
            Fields = new Dictionary<string, string> { ["apiKey"] = "new-value" },
        };

        var (result, created) = await _service.EnsureAsync(dto, ct);

        Assert.False(created);
        Assert.Equal(existing.Id, result.Id);
        Assert.Equal("new-value", result.Fields["apiKey"]);

        var updated = await _dbContext.Credentials.FindAsync([existing.Id], ct);
        Assert.NotNull(updated);
        Assert.Equal("encrypted:new-value", updated.Data["apiKey"].CipherText);
    }

    [Fact]
    public async Task EnsureAsync_ExistingCredential_PublishesCredentialUpdatedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var existing = CreateTestCredential("Ensure Update Event");
        existing.Data = new Dictionary<string, EncryptedField>
        {
            ["apiKey"] = _encryptionService.Encrypt("old-value", _keyProvider.GetKey()),
        };
        _dbContext.Credentials.Add(existing);
        await _dbContext.SaveChangesAsync(ct);

        var dto = new CreateCredentialDto
        {
            Name = existing.Name,
            Type = existing.Type,
            Fields = new Dictionary<string, string> { ["apiKey"] = "updated-value" },
        };

        await _service.EnsureAsync(dto, ct);

        var updatedEvent = _eventBus.PublishedEvents
            .OfType<AuditLogEvent>()
            .FirstOrDefault(e => e.EventType == AuditEventTypes.CredentialUpdated);
        Assert.NotNull(updatedEvent);
        Assert.Equal(existing.Id, updatedEvent.ResourceId);
    }

    [Fact]
    public async Task EnsureAsync_NewCredential_PublishesCredentialCreatedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var dto = new CreateCredentialDto
        {
            Name = "Ensure Create Event",
            Type = "apiKey",
            Fields = new Dictionary<string, string> { ["apiKey"] = "sk-event" },
        };

        var (result, _) = await _service.EnsureAsync(dto, ct);

        var createdEvent = _eventBus.PublishedEvents
            .OfType<AuditLogEvent>()
            .FirstOrDefault(e => e.EventType == AuditEventTypes.CredentialCreated);
        Assert.NotNull(createdEvent);
        Assert.Equal(result.Id, createdEvent.ResourceId);
    }

    [Fact]
    public async Task EnsureAsync_DifferentiatesByProjectId()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var existing = CreateTestCredential("Same Name Type", projectId: projectId);
        existing.Data = new Dictionary<string, EncryptedField>
        {
            ["apiKey"] = _encryptionService.Encrypt("old-value", _keyProvider.GetKey()),
        };
        _dbContext.Credentials.Add(existing);
        _dbContext.Credentials.Add(CreateTestCredential("Same Name Type"));
        await _dbContext.SaveChangesAsync(ct);

        var dto = new CreateCredentialDto
        {
            Name = existing.Name,
            Type = existing.Type,
            ProjectId = projectId,
            Fields = new Dictionary<string, string> { ["apiKey"] = "new-value" },
        };

        var (result, created) = await _service.EnsureAsync(dto, ct);

        Assert.False(created);
        Assert.Equal(existing.Id, result.Id);
    }

    [Fact]
    public async Task EnsureAsync_UnauthorizedRole_ThrowsPermissionDeniedException()
    {
        var ct = TestContext.Current.CancellationToken;
        _userContext.Roles = [RoleConstants.Viewer];

        var dto = new CreateCredentialDto
        {
            Name = "Ensure",
            Type = "apiKey",
            Fields = [],
        };

        await Assert.ThrowsAsync<PermissionDeniedException>(() => _service.EnsureAsync(dto, ct));
    }

    [Fact]
    public async Task EnsureAsync_NullDto_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.EnsureAsync(null!, ct));
    }

    [Fact]
    public async Task CreateAsync_UnknownType_ThrowsBusinessException()
    {
        var ct = TestContext.Current.CancellationToken;
        var dto = new CreateCredentialDto
        {
            Name = "Test",
            Type = "unknownType",
            Fields = new Dictionary<string, string> { ["apiKey"] = "sk-test" },
        };

        var exception = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateAsync(dto, ct));
        Assert.Contains("未知凭据类型", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_MissingRequiredFields_ThrowsBusinessException()
    {
        var ct = TestContext.Current.CancellationToken;
        var dto = new CreateCredentialDto
        {
            Name = "Test",
            Type = "oauth2",
            Fields = new Dictionary<string, string> { ["tokenUrl"] = "https://example.com/token" },
        };

        var exception = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateAsync(dto, ct));
        Assert.Contains("缺少必填字段", exception.Message);
        Assert.Contains("clientId", exception.Message);
        Assert.Contains("clientSecret", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_InvalidOAuth2Provider_ThrowsBusinessException()
    {
        var ct = TestContext.Current.CancellationToken;
        var dto = new CreateCredentialDto
        {
            Name = "Test",
            Type = "oauth2",
            Fields = new Dictionary<string, string>
            {
                ["tokenUrl"] = "https://example.com/token",
                ["clientId"] = "client-id",
                ["clientSecret"] = "client-secret",
                ["provider"] = "unknown"
            },
        };

        var exception = await Assert.ThrowsAsync<BusinessException>(() => _service.CreateAsync(dto, ct));
        Assert.Contains("provider", exception.Message);
        Assert.Contains("unknown", exception.Message);
    }

    [Fact]
    public async Task EnsureAsync_UnknownType_ThrowsBusinessException()
    {
        var ct = TestContext.Current.CancellationToken;
        var dto = new CreateCredentialDto
        {
            Name = "Test",
            Type = "unknownType",
            Fields = new Dictionary<string, string> { ["apiKey"] = "sk-test" },
        };

        var exception = await Assert.ThrowsAsync<BusinessException>(() => _service.EnsureAsync(dto, ct));
        Assert.Contains("未知凭据类型", exception.Message);
    }

    [Fact]
    public async Task EnsureAsync_MissingRequiredFields_ThrowsBusinessException()
    {
        var ct = TestContext.Current.CancellationToken;
        var dto = new CreateCredentialDto
        {
            Name = "Test",
            Type = "basicAuth",
            Fields = new Dictionary<string, string> { ["username"] = "user" },
        };

        var exception = await Assert.ThrowsAsync<BusinessException>(() => _service.EnsureAsync(dto, ct));
        Assert.Contains("缺少必填字段", exception.Message);
        Assert.Contains("password", exception.Message);
    }

    [Fact]
    public async Task GetAsync_KeyVersioned_ResolvesPerVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        var keyProvider = new VersionedTestKeyProvider();
        var encryption = new CredentialEncryptionService();
        var service = BuildService(keyProvider, encryption);

        // v1 加密、KeyVersion=v1 → 解密成功
        var v1Data = new Dictionary<string, EncryptedField>
        {
            ["token"] = encryption.Encrypt("secret-v1", keyProvider.GetKey("v1")),
        };
        var credV1 = new Credential { Id = Guid.NewGuid(), Name = "c1", Type = "apiKey", Data = v1Data, KeyVersion = "v1" };
        _dbContext.Credentials.Add(credV1);
        await _dbContext.SaveChangesAsync(ct);

        var r1 = (await service.GetAsync(credV1.Id, ct))!;
        Assert.Equal("secret-v1", r1.Fields["token"]);

        // v2 加密、KeyVersion=v2 → 解密成功
        var v2Data = new Dictionary<string, EncryptedField>
        {
            ["token"] = encryption.Encrypt("secret-v2", keyProvider.GetKey("v2")),
        };
        var credV2 = new Credential { Id = Guid.NewGuid(), Name = "c2", Type = "apiKey", Data = v2Data, KeyVersion = "v2" };
        _dbContext.Credentials.Add(credV2);
        await _dbContext.SaveChangesAsync(ct);

        var r2 = (await service.GetAsync(credV2.Id, ct))!;
        Assert.Equal("secret-v2", r2.Fields["token"]);

        // v2 数据但 KeyVersion=v1（版本错配）→ v1 密钥解密失败
        var wrong = new Credential { Id = Guid.NewGuid(), Name = "c3", Type = "apiKey", Data = v2Data, KeyVersion = "v1" };
        _dbContext.Credentials.Add(wrong);
        await _dbContext.SaveChangesAsync(ct);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => service.GetAsync(wrong.Id, ct));
    }

    [Fact]
    public async Task GetAsync_EmptyKeyVersion_FallsBackToCurrent()
    {
        var ct = TestContext.Current.CancellationToken;
        var keyProvider = new VersionedTestKeyProvider();
        var encryption = new CredentialEncryptionService();
        var service = BuildService(keyProvider, encryption);

        var data = new Dictionary<string, EncryptedField>
        {
            ["token"] = encryption.Encrypt("secret", keyProvider.GetKey("v1")),
        };
        // 空 KeyVersion 视为当前版本（兼容未带版本的遗留数据），解密不应抛异常。
        var credEmpty = new Credential { Id = Guid.NewGuid(), Name = "c4", Type = "apiKey", Data = data, KeyVersion = string.Empty };
        _dbContext.Credentials.Add(credEmpty);
        await _dbContext.SaveChangesAsync(ct);

        var rEmpty = (await service.GetAsync(credEmpty.Id, ct))!;
        Assert.Equal("secret", rEmpty.Fields["token"]);
    }

    private CredentialService BuildService(ICryptoKeyProvider keyProvider, ICredentialEncryptionService encryption)
    {
        var auditFactory = new AuditEventFactory(_userContext);
        var resourceAuthService = new StubResourceAuthorizationService();
        var authGuard = AuthorizationGuardFactory.Create(_userContext, resourceAuthService);
        var handler = new AuthorizedOperationHandler(authGuard, _eventBus, auditFactory);
        return new CredentialService(
            _dbContext,
            encryption,
            keyProvider,
            _eventBus,
            auditFactory,
            resourceAuthService,
            _userContext,
            new WorkflowRepository(_dbContext),
            authGuard,
            new CredentialTypeRegistry(),
            handler);
    }

    private static Credential CreateTestCredential(string? name = null, Guid? id = null, Guid? projectId = null, string type = "apiKey")
    {
        return new Credential
        {
            Id = id ?? Guid.NewGuid(),
            ProjectId = projectId,
            Name = name ?? "Test Credential",
            Type = type,
            Data = new Dictionary<string, EncryptedField>(),
            KeyVersion = "v1",
        };
    }

    private sealed class StubResourceAuthorizationService : IResourceAuthorizationService
    {
        public Task<bool> CanAccessWorkflowAsync(Guid userId, Guid workflowId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> CanAccessCredentialAsync(Guid userId, Guid credentialId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> CanAccessExecutionAsync(Guid userId, Guid executionId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> CanAccessTriggerAsync(Guid userId, Guid triggerId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(true);

        public bool ShouldMaskCredentialValues(IReadOnlyList<string> roles) => false;
    }
}
