using FlowEngine.Application.Audit;
using FlowEngine.Application.Credentials;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Events;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Tests.Credentials;

public sealed class CredentialServiceTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly InMemoryEventBus _eventBus;
    private readonly CredentialService _service;
    private readonly StubEncryptionService _encryptionService;
    private readonly StubKeyProvider _keyProvider;

    public CredentialServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _eventBus = new InMemoryEventBus();
        _encryptionService = new StubEncryptionService();
        _keyProvider = new StubKeyProvider();
        var userContext = new FakeUserContext();
        var auditFactory = new AuditEventFactory(userContext);
        _service = new CredentialService(_dbContext, _encryptionService, _keyProvider, _eventBus, auditFactory);
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
        _dbContext.Credentials.AddRange(
            CreateTestCredential("Key 1"),
            CreateTestCredential("Key 2"));
        await _dbContext.SaveChangesAsync(ct);

        var results = await _service.GetAllAsync(ct);

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

    private static Credential CreateTestCredential(string? name = null, Guid? id = null)
    {
        return new Credential
        {
            Id = id ?? Guid.NewGuid(),
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
        public IReadOnlyList<string> Roles => [];
    }
}
