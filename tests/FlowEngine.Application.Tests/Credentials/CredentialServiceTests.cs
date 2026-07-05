using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Credentials;
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

namespace FlowEngine.Application.Tests.Credentials;

public sealed class CredentialServiceTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly InMemoryEventBus _eventBus;
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
        _eventBus = new InMemoryEventBus();
        _encryptionService = new StubEncryptionService();
        _keyProvider = new StubKeyProvider();
        _userContext = new FakeUserContext();
        var auditFactory = new AuditEventFactory(_userContext);
        var resourceAuthService = new StubResourceAuthorizationService();
        _service = new CredentialService(_dbContext, _encryptionService, _keyProvider, _eventBus, auditFactory, resourceAuthService, _userContext, new WorkflowRepository(_dbContext));
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
            Fields = new Dictionary<string, string> { ["key"] = "sk-123456" },
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
            Fields = new Dictionary<string, string> { ["key"] = "new-value" },
        };

        var result = await _service.UpdateAsync(credential.Id, dto, ct);

        Assert.NotNull(result);
        Assert.Equal("Updated", result.Name);

        var updated = await _dbContext.Credentials.FindAsync([credential.Id], ct);
        Assert.NotNull(updated);
        Assert.Equal("encrypted:new-value", updated.Data["key"].CipherText);
    }

    [Fact]
    public async Task UpdateAsync_NonExistingCredential_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var dto = new UpdateCredentialDto { Name = "Test" };
        var result = await _service.UpdateAsync(Guid.NewGuid(), dto, ct);
        Assert.Null(result);
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
            Fields = new Dictionary<string, string> { ["key"] = "sk-ensure" },
        };

        var (result, created) = await _service.EnsureAsync(dto, ct);

        Assert.True(created);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Ensure Key", result.Name);
        Assert.Equal("apiKey", result.Type);
        Assert.Equal("sk-ensure", result.Fields["key"]);
    }

    [Fact]
    public async Task EnsureAsync_ExistingCredential_UpdatesFieldsAndReturnsNotCreated()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var existing = CreateTestCredential("Ensure Existing", projectId: projectId);
        existing.Data = new Dictionary<string, EncryptedField>
        {
            ["key"] = _encryptionService.Encrypt("old-value", _keyProvider.GetKey()),
        };
        _dbContext.Credentials.Add(existing);
        await _dbContext.SaveChangesAsync(ct);

        var dto = new CreateCredentialDto
        {
            Name = existing.Name,
            Type = existing.Type,
            ProjectId = projectId,
            Fields = new Dictionary<string, string> { ["key"] = "new-value" },
        };

        var (result, created) = await _service.EnsureAsync(dto, ct);

        Assert.False(created);
        Assert.Equal(existing.Id, result.Id);
        Assert.Equal("new-value", result.Fields["key"]);

        var updated = await _dbContext.Credentials.FindAsync([existing.Id], ct);
        Assert.NotNull(updated);
        Assert.Equal("encrypted:new-value", updated.Data["key"].CipherText);
    }

    [Fact]
    public async Task EnsureAsync_ExistingCredential_PublishesCredentialUpdatedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var existing = CreateTestCredential("Ensure Update Event");
        _dbContext.Credentials.Add(existing);
        await _dbContext.SaveChangesAsync(ct);

        var dto = new CreateCredentialDto
        {
            Name = existing.Name,
            Type = existing.Type,
            Fields = [],
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
            Fields = [],
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
        _dbContext.Credentials.Add(existing);
        _dbContext.Credentials.Add(CreateTestCredential("Same Name Type"));
        await _dbContext.SaveChangesAsync(ct);

        var dto = new CreateCredentialDto
        {
            Name = existing.Name,
            Type = existing.Type,
            ProjectId = projectId,
            Fields = [],
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

    private static Credential CreateTestCredential(string? name = null, Guid? id = null, Guid? projectId = null)
    {
        return new Credential
        {
            Id = id ?? Guid.NewGuid(),
            ProjectId = projectId,
            Name = name ?? "Test Credential",
            Type = "apiKey",
            Data = new Dictionary<string, EncryptedField>(),
            KeyVersion = "v1",
        };
    }

    private sealed class StubEncryptionService : ICredentialEncryptionService
    {
        public EncryptedField Encrypt(string plaintext, byte[] key)
        {
            return new EncryptedField
            {
                CipherText = $"encrypted:{plaintext}",
                Nonce = "nonce",
                Tag = "tag",
            };
        }

        public EncryptedField Encrypt(byte[] plaintext, byte[] key) =>
            new() { CipherText = Convert.ToBase64String(plaintext), Nonce = "nonce", Tag = "tag" };

        public string DecryptString(EncryptedField field, byte[] key) =>
            field.CipherText.Replace("encrypted:", "");

        public byte[] DecryptBytes(EncryptedField field, byte[] key) =>
            Convert.FromBase64String(field.CipherText);
    }

    private sealed class StubKeyProvider : ICryptoKeyProvider
    {
        public byte[] GetKey() => new byte[32];
    }

    private sealed class InMemoryEventBus : IEventBus
    {
        public List<object> PublishedEvents { get; } = [];

        public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
        {
            PublishedEvents.Add(eventInstance!);
            return Task.CompletedTask;
        }

        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
            where TEvent : IDomainEvent => new Disposable();

        private sealed class Disposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class FakeUserContext : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => Guid.NewGuid();
        public string? Email => "test@test.com";
        public IReadOnlyList<string> Roles { get; set; } = ["Admin"];
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
