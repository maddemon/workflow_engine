using FlowEngine.Application.Audit;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Events;
using FlowEngine.Core.Identity;
using FlowEngine.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Application.Tests.Identity;

/// <summary>
/// ApiKeyService 测试 —— 覆盖创建、列出、吊销、验证。
/// </summary>
public class ApiKeyServiceTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly ApiKeyService _apiKeyService;
    private readonly StubEventBus _eventBus;
    private readonly StubUserContext _userContext;

    /// <summary>
    /// 初始化测试，创建 SQLite 内存数据库。
    /// </summary>
    public ApiKeyServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new FlowEngineDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _eventBus = new StubEventBus();
        _userContext = new StubUserContext();
        var auditFactory = new AuditEventFactory(_userContext);
        _apiKeyService = new ApiKeyService(_dbContext, _eventBus, auditFactory, NullLogger<ApiKeyService>.Instance);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task CreateAsync_ValidName_ReturnsPlaintextKeyAndStoresHash()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await CreateUserAsync("create@example.com", ct);
        _userContext.SetUser(user.Id);

        var result = await _apiKeyService.CreateAsync(user.Id, "CLI Key", null, ct);

        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("CLI Key", result.Name);
        Assert.StartsWith("fe_", result.Key);
        Assert.Equal(result.Key[..Math.Min(8, result.Key.Length)], result.Prefix);
        Assert.Null(result.ExpiresAt);

        var stored = await _dbContext.Set<ApiKey>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == result.Id, ct);
        Assert.NotNull(stored);
        Assert.NotEqual(result.Key, stored!.KeyHash);
        Assert.True(stored.KeyHash.Length > 0);
        Assert.Null(stored.RevokedAt);
    }

    [Fact]
    public async Task CreateAsync_WithExpiration_ReturnsExpiresAt()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await CreateUserAsync("expire@example.com", ct);
        _userContext.SetUser(user.Id);
        var expiresAt = DateTime.UtcNow.AddDays(30);

        var result = await _apiKeyService.CreateAsync(user.Id, "Temp Key", expiresAt, ct);

        Assert.NotNull(result);
        Assert.True((expiresAt - result.ExpiresAt!.Value).Duration() < TimeSpan.FromSeconds(1));

        var stored = await _dbContext.Set<ApiKey>().AsNoTracking().FirstAsync(x => x.Id == result.Id, ct);
        Assert.True((expiresAt - stored.ExpiresAt!.Value).Duration() < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CreateAsync_EmptyName_ThrowsArgumentException()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await CreateUserAsync("emptyname@example.com", ct);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _apiKeyService.CreateAsync(user.Id, "", null, ct));
    }

    [Fact]
    public async Task CreateAsync_PublishesAuditEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await CreateUserAsync("auditcreate@example.com", ct);
        _userContext.SetUser(user.Id);

        var result = await _apiKeyService.CreateAsync(user.Id, "Audit Key", null, ct);

        var published = _eventBus.PublishedEvents.OfType<AuditLogEvent>().ToList();
        Assert.Single(published);
        Assert.Equal(AuditEventTypes.ApiKeyCreated, published[0].EventType);
        Assert.Equal(result.Id, published[0].ResourceId);
        Assert.Equal("ApiKey", published[0].ResourceType);
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyOwnersKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        var userA = await CreateUserAsync("ownerA@example.com", ct);
        var userB = await CreateUserAsync("ownerB@example.com", ct);
        _userContext.SetUser(userA.Id);

        await _apiKeyService.CreateAsync(userA.Id, "A Key", null, ct);
        await _apiKeyService.CreateAsync(userB.Id, "B Key", null, ct);

        var list = await _apiKeyService.ListAsync(userA.Id, ct);

        Assert.Single(list);
        Assert.Equal("A Key", list[0].Name);
    }

    [Fact]
    public async Task ListAsync_DoesNotReturnKeyPlaintext()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await CreateUserAsync("list@example.com", ct);
        _userContext.SetUser(user.Id);

        await _apiKeyService.CreateAsync(user.Id, "List Key", null, ct);

        var list = await _apiKeyService.ListAsync(user.Id, ct);

        Assert.Single(list);
        var dtoType = list[0].GetType();
        Assert.Null(dtoType.GetProperty("Key")?.GetValue(list[0]));
        Assert.False(string.IsNullOrEmpty(list[0].Prefix));
    }

    [Fact]
    public async Task RevokeAsync_ExistingKey_SetsRevokedAtAndPublishesEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await CreateUserAsync("revoke@example.com", ct);
        _userContext.SetUser(user.Id);

        var created = await _apiKeyService.CreateAsync(user.Id, "Revoke Key", null, ct);
        _eventBus.PublishedEvents.Clear();

        var result = await _apiKeyService.RevokeAsync(user.Id, created.Id, ct);

        Assert.True(result);
        var stored = await _dbContext.Set<ApiKey>().AsNoTracking().FirstAsync(x => x.Id == created.Id, ct);
        Assert.NotNull(stored.RevokedAt);

        var published = _eventBus.PublishedEvents.OfType<AuditLogEvent>().ToList();
        Assert.Single(published);
        Assert.Equal(AuditEventTypes.ApiKeyRevoked, published[0].EventType);
        Assert.Equal(created.Id, published[0].ResourceId);
    }

    [Fact]
    public async Task RevokeAsync_OtherUsersKey_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var userA = await CreateUserAsync("revokeA@example.com", ct);
        var userB = await CreateUserAsync("revokeB@example.com", ct);
        _userContext.SetUser(userA.Id);

        var created = await _apiKeyService.CreateAsync(userA.Id, "A Key", null, ct);

        var result = await _apiKeyService.RevokeAsync(userB.Id, created.Id, ct);

        Assert.False(result);
        var stored = await _dbContext.Set<ApiKey>().AsNoTracking().FirstAsync(x => x.Id == created.Id, ct);
        Assert.Null(stored.RevokedAt);
    }

    [Fact]
    public async Task RevokeAsync_AlreadyRevoked_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await CreateUserAsync("revokedtwice@example.com", ct);
        _userContext.SetUser(user.Id);

        var created = await _apiKeyService.CreateAsync(user.Id, "Twice Key", null, ct);
        await _apiKeyService.RevokeAsync(user.Id, created.Id, ct);

        var result = await _apiKeyService.RevokeAsync(user.Id, created.Id, ct);

        Assert.False(result);
    }

    [Fact]
    public async Task ValidateAsync_ValidKey_ReturnsUserId()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await CreateUserAsync("validate@example.com", ct);
        _userContext.SetUser(user.Id);

        var created = await _apiKeyService.CreateAsync(user.Id, "Validate Key", null, ct);

        var userId = await _apiKeyService.ValidateAsync(created.Key, ct);

        Assert.Equal(user.Id, userId);
    }

    [Fact]
    public async Task ValidateAsync_RevokedKey_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await CreateUserAsync("revokedvalidate@example.com", ct);
        _userContext.SetUser(user.Id);

        var created = await _apiKeyService.CreateAsync(user.Id, "Revoked Validate Key", null, ct);
        await _apiKeyService.RevokeAsync(user.Id, created.Id, ct);

        var userId = await _apiKeyService.ValidateAsync(created.Key, ct);

        Assert.Null(userId);
    }

    [Fact]
    public async Task ValidateAsync_ExpiredKey_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await CreateUserAsync("expiredvalidate@example.com", ct);
        _userContext.SetUser(user.Id);

        var created = await _apiKeyService.CreateAsync(
            user.Id,
            "Expired Validate Key",
            DateTime.UtcNow.AddDays(-1),
            ct);

        var userId = await _apiKeyService.ValidateAsync(created.Key, ct);

        Assert.Null(userId);
    }

    [Fact]
    public async Task ValidateAsync_InvalidKey_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;

        var userId = await _apiKeyService.ValidateAsync("fe_invalid_key", ct);

        Assert.Null(userId);
    }

    [Fact]
    public async Task ValidateAsync_DeletedUser_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await CreateUserAsync("deletedvalidate@example.com", ct);
        _userContext.SetUser(user.Id);

        var created = await _apiKeyService.CreateAsync(user.Id, "Deleted User Key", null, ct);
        user.IsActive = false;
        _dbContext.Set<User>().Update(user);
        await _dbContext.SaveChangesAsync(ct);

        var userId = await _apiKeyService.ValidateAsync(created.Key, ct);

        Assert.Null(userId);
    }

    private async Task<User> CreateUserAsync(string email, CancellationToken ct)
    {
        var passwordHasher = new PasswordHasher();
        var user = new User
        {
            Email = email,
            UserName = email.Split('@')[0],
            DisplayName = email,
            PasswordHash = passwordHasher.HashPassword("StrongP@ss1"),
            IsActive = true,
        };
        _dbContext.Set<User>().Add(user);
        await _dbContext.SaveChangesAsync(ct);
        return user;
    }

    private sealed class StubEventBus : IEventBus
    {
        public List<IDomainEvent> PublishedEvents { get; } = [];

        public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
        {
            PublishedEvents.Add(eventInstance);
            return Task.CompletedTask;
        }

        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
            where TEvent : IDomainEvent
            => new StubSubscription();

        private sealed class StubSubscription : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class StubUserContext : IUserContext
    {
        private Guid? _userId;

        public bool IsAuthenticated => _userId.HasValue;

        public Guid? UserId => _userId;

        public string? Email => null;

        public IReadOnlyList<string> Roles => [];

        public void SetUser(Guid userId) => _userId = userId;
    }
}
