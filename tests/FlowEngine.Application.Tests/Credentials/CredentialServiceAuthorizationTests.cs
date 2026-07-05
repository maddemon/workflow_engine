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

public sealed class CredentialServiceAuthorizationTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly InMemoryEventBus _eventBus;
    private readonly CredentialService _service;
    private readonly FakeUserContext _userContext;

    public CredentialServiceAuthorizationTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _eventBus = new InMemoryEventBus();
        _userContext = new FakeUserContext();
        var auditFactory = new AuditEventFactory(_userContext);
        var resourceAuthService = new RoleBasedResourceAuthorizationService(_userContext);
        _service = new CredentialService(
            _dbContext,
            new StubEncryptionService(),
            new StubKeyProvider(),
            _eventBus,
            auditFactory,
            resourceAuthService,
            _userContext,
            new WorkflowRepository(_dbContext));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetAsync_UnauthenticatedUser_ThrowsPermissionDeniedException()
    {
        var ct = TestContext.Current.CancellationToken;
        _userContext.UserId = null;

        await Assert.ThrowsAsync<PermissionDeniedException>(() => _service.GetAsync(Guid.NewGuid(), ct));
    }

    [Fact]
    public async Task GetAsync_Viewer_CanReadExistingCredential()
    {
        var ct = TestContext.Current.CancellationToken;
        var credential = CreateTestCredential();
        _dbContext.Credentials.Add(credential);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Viewer];

        var result = await _service.GetAsync(credential.Id, ct);

        Assert.NotNull(result);
        Assert.Equal(credential.Id, result.Id);
    }

    [Fact]
    public async Task UpdateAsync_Viewer_ThrowsPermissionDeniedException()
    {
        var ct = TestContext.Current.CancellationToken;
        var credential = CreateTestCredential();
        _dbContext.Credentials.Add(credential);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Viewer];

        var dto = new UpdateCredentialDto
        {
            Name = "Updated",
            Fields = new Dictionary<string, string> { ["key"] = "new-value" },
        };

        await Assert.ThrowsAsync<PermissionDeniedException>(() => _service.UpdateAsync(credential.Id, dto, ct));
    }

    [Fact]
    public async Task UpdateAsync_Editor_UpdatesCredential()
    {
        var ct = TestContext.Current.CancellationToken;
        var credential = CreateTestCredential();
        _dbContext.Credentials.Add(credential);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Editor];

        var dto = new UpdateCredentialDto
        {
            Name = "Updated",
            Fields = new Dictionary<string, string> { ["key"] = "new-value" },
        };

        var result = await _service.UpdateAsync(credential.Id, dto, ct);

        Assert.NotNull(result);
        Assert.Equal("Updated", result.Name);
    }

    [Fact]
    public async Task DeleteAsync_Viewer_ThrowsPermissionDeniedException()
    {
        var ct = TestContext.Current.CancellationToken;
        var credential = CreateTestCredential();
        _dbContext.Credentials.Add(credential);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Viewer];

        await Assert.ThrowsAsync<PermissionDeniedException>(() => _service.DeleteAsync(credential.Id, ct));
    }

    [Fact]
    public async Task DeleteAsync_Admin_DeletesCredential()
    {
        var ct = TestContext.Current.CancellationToken;
        var credential = CreateTestCredential();
        _dbContext.Credentials.Add(credential);
        await _dbContext.SaveChangesAsync(ct);
        _userContext.Roles = [RoleConstants.Admin];

        var result = await _service.DeleteAsync(credential.Id, ct);

        Assert.True(result.Deleted);
        var deleted = await _dbContext.Credentials.FindAsync([credential.Id], ct);
        Assert.Null(deleted);
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
        public bool IsAuthenticated => UserId.HasValue;
        public Guid? UserId { get; set; } = Guid.NewGuid();
        public string? Email => "test@test.com";
        public IReadOnlyList<string> Roles { get; set; } = [];
    }

    private sealed class RoleBasedResourceAuthorizationService(IUserContext userContext) : IResourceAuthorizationService
    {
        public Task<bool> CanAccessWorkflowAsync(Guid userId, Guid workflowId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(IsAllowed(operation));

        public Task<bool> CanAccessCredentialAsync(Guid userId, Guid credentialId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(IsAllowed(operation));

        public Task<bool> CanAccessExecutionAsync(Guid userId, Guid executionId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(IsAllowed(operation));

        public Task<bool> CanAccessTriggerAsync(Guid userId, Guid triggerId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(IsAllowed(operation));

        public bool ShouldMaskCredentialValues(IReadOnlyList<string> roles) => false;

        private bool IsAllowed(Operation operation)
        {
            var roles = userContext.Roles;
            return operation switch
            {
                Operation.Read => roles.Contains(RoleConstants.Admin) || roles.Contains(RoleConstants.Editor) || roles.Contains(RoleConstants.Viewer),
                Operation.Write => roles.Contains(RoleConstants.Admin) || roles.Contains(RoleConstants.Editor),
                Operation.Delete or Operation.Execute => roles.Contains(RoleConstants.Admin),
                _ => false,
            };
        }
    }
}
