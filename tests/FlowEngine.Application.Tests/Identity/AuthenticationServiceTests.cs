using FlowEngine.Application.Audit;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Events;
using FlowEngine.Core.Identity;
using FlowEngine.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FlowEngine.Application.Tests.Identity;

/// <summary>
/// 认证服务测试 —— 覆盖注册、登录、密码哈希、密码强度校验。
/// </summary>
public class AuthenticationServiceTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly UserStore _userStore;
    private readonly PasswordHasher _passwordHasher;
    private readonly PasswordValidator _passwordValidator;
    private readonly StubTokenService _tokenService;
    private readonly AuthenticationService _authService;
    private readonly MemoryCache _memoryCache;

    /// <summary>
    /// 初始化测试，创建 SQLite 内存数据库。
    /// </summary>
    public AuthenticationServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new FlowEngineDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _userStore = new UserStore(_dbContext);
        _passwordHasher = new PasswordHasher();
        _passwordValidator = new PasswordValidator();
        _tokenService = new StubTokenService();
        var eventBus = new StubEventBus();
        var auditFactory = new AuditEventFactory(new StubUserContext());
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _authService = new AuthenticationService(_userStore, _passwordHasher, _passwordValidator, _tokenService, eventBus, auditFactory, _memoryCache);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
        _memoryCache.Dispose();
    }

    [Fact]
    public async Task RegisterAsync_ValidInput_CreatesUserAndReturnsSuccess()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new RegisterRequest
        {
            Email = "newuser@example.com",
            UserName = "newuser",
            Password = "StrongP@ss1",
            DisplayName = "New User",
        };

        var result = await _authService.RegisterAsync(request, ct);

        Assert.True(result.Success);
        Assert.NotNull(result.UserId);
        Assert.NotEqual(Guid.Empty, result.UserId);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new RegisterRequest
        {
            Email = "dup@example.com",
            UserName = "dupuser",
            Password = "StrongP@ss1",
        };

        await _authService.RegisterAsync(request, ct);

        var duplicate = new RegisterRequest
        {
            Email = "dup@example.com",
            UserName = "dupuser2",
            Password = "StrongP@ss2",
        };

        var result = await _authService.RegisterAsync(duplicate, ct);

        Assert.False(result.Success);
        Assert.Equal(RegisterResultErrors.EmailAlreadyExists, result.ErrorMessage);
    }

    [Fact]
    public async Task RegisterAsync_WeakPassword_ReturnsValidationError()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new RegisterRequest
        {
            Email = "weak@example.com",
            UserName = "weakuser",
            Password = "short",
        };

        var result = await _authService.RegisterAsync(request, ct);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task RegisterAsync_InvalidEmailFormat_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new RegisterRequest
        {
            Email = "not-an-email",
            UserName = "baduser",
            Password = "StrongP@ss1",
        };

        var result = await _authService.RegisterAsync(request, ct);

        Assert.False(result.Success);
        Assert.Equal("邮箱格式无效", result.ErrorMessage);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsToken()
    {
        var ct = TestContext.Current.CancellationToken;
        var registerRequest = new RegisterRequest
        {
            Email = "login@example.com",
            UserName = "loginuser",
            Password = "StrongP@ss1",
        };

        var registerResult = await _authService.RegisterAsync(registerRequest, ct);
        Assert.True(registerResult.Success);

        var loginRequest = new LoginRequest
        {
            Email = "login@example.com",
            Password = "StrongP@ss1",
        };

        var loginResult = await _authService.LoginAsync(loginRequest, ct);

        Assert.True(loginResult.Success);
        Assert.NotNull(loginResult.Token);
        Assert.Equal("stub-token", loginResult.Token);
        Assert.Equal(registerResult.UserId, loginResult.UserId);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        await _authService.RegisterAsync(new RegisterRequest
        {
            Email = "wrongpw@example.com",
            UserName = "wrongpwuser",
            Password = "StrongP@ss1",
        }, ct);

        var loginResult = await _authService.LoginAsync(new LoginRequest
        {
            Email = "wrongpw@example.com",
            Password = "WrongP@ss2",
        }, ct);

        Assert.False(loginResult.Success);
        Assert.Equal("邮箱或密码错误", loginResult.ErrorMessage);
        Assert.Null(loginResult.Token);
    }

    [Fact]
    public async Task LoginAsync_NonExistentUser_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        var loginResult = await _authService.LoginAsync(new LoginRequest
        {
            Email = "nonexistent@example.com",
            Password = "StrongP@ss1",
        }, ct);

        Assert.False(loginResult.Success);
        Assert.Equal("邮箱或密码错误", loginResult.ErrorMessage);
    }

    [Fact]
    public async Task LoginAsync_EmptyCredentials_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        var loginResult = await _authService.LoginAsync(new LoginRequest
        {
            Email = "",
            Password = "",
        }, ct);

        Assert.False(loginResult.Success);
        Assert.Equal("邮箱和密码不能为空", loginResult.ErrorMessage);
    }

    [Fact]
    public async Task LoginAsync_ExcessiveFailedAttempts_LocksAccount()
    {
        var ct = TestContext.Current.CancellationToken;
        await _authService.RegisterAsync(new RegisterRequest
        {
            Email = "lockout@example.com",
            UserName = "lockoutuser",
            Password = "StrongP@ss1",
        }, ct);

        for (var i = 0; i < 5; i++)
        {
            await _authService.LoginAsync(new LoginRequest
            {
                Email = "lockout@example.com",
                Password = "WrongP@ss2",
            }, ct);
        }

        var lockedResult = await _authService.LoginAsync(new LoginRequest
        {
            Email = "lockout@example.com",
            Password = "StrongP@ss1",
        }, ct);

        Assert.False(lockedResult.Success);
        Assert.Contains("登录尝试过多", lockedResult.ErrorMessage);
    }

    [Fact]
    public async Task LoginAsync_SuccessAfterFailedAttempts_ClearsAttempts()
    {
        var ct = TestContext.Current.CancellationToken;
        await _authService.RegisterAsync(new RegisterRequest
        {
            Email = "clearattempts@example.com",
            UserName = "clearattemptsuser",
            Password = "StrongP@ss1",
        }, ct);

        for (var i = 0; i < 4; i++)
        {
            await _authService.LoginAsync(new LoginRequest
            {
                Email = "clearattempts@example.com",
                Password = "WrongP@ss2",
            }, ct);
        }

        var successResult = await _authService.LoginAsync(new LoginRequest
        {
            Email = "clearattempts@example.com",
            Password = "StrongP@ss1",
        }, ct);

        Assert.True(successResult.Success);

        for (var i = 0; i < 5; i++)
        {
            await _authService.LoginAsync(new LoginRequest
            {
                Email = "clearattempts@example.com",
                Password = "WrongP@ss2",
            }, ct);
        }

        var lockedResult = await _authService.LoginAsync(new LoginRequest
        {
            Email = "clearattempts@example.com",
            Password = "StrongP@ss1",
        }, ct);

        Assert.False(lockedResult.Success);
        Assert.Contains("登录尝试过多", lockedResult.ErrorMessage);
    }

    [Fact]
    public void PasswordHasher_HashAndVerify_ReturnsTrueForCorrectPassword()
    {
        var password = "TestP@ss123";
        var hash = _passwordHasher.HashPassword(password);

        Assert.NotNull(hash);
        Assert.NotEqual(password, hash);
        Assert.NotEqual(PasswordVerifyResult.Failed, _passwordHasher.VerifyPassword(hash, password));
    }

    [Fact]
    public void PasswordHasher_HashAndVerify_ReturnsFalseForWrongPassword()
    {
        var hash = _passwordHasher.HashPassword("CorrectP@ss1");

        Assert.Equal(PasswordVerifyResult.Failed, _passwordHasher.VerifyPassword(hash, "WrongP@ss2"));
    }

    [Fact]
    public void PasswordHasher_VerifyPassword_LegacyV2Hash_ReturnsSuccessRehashNeeded()
    {
        // 存量用户可能使用旧版（IdentityV2/SHA1）算法哈希；当前默认 V3 校验器应判定为
        // 成功但需升级（SuccessRehashNeeded），而非失败。
        var password = "LegacyP@ss1";
        var legacyHash = CreateLegacyV2Hash(password);

        var result = _passwordHasher.VerifyPassword(legacyHash, password);

        Assert.Equal(PasswordVerifyResult.SuccessRehashNeeded, result);
    }

    [Fact]
    public async Task LoginAsync_LegacyV2HashAlgorithm_SucceedsAndUpgradesHash()
    {
        var ct = TestContext.Current.CancellationToken;
        const string email = "legacy@example.com";
        const string password = "LegacyP@ss1";

        // 模拟算法升级前的存量用户：密码以旧版 V2 哈希存储。
        var user = new User
        {
            Email = email,
            UserName = "legacyuser",
            DisplayName = "Legacy User",
            PasswordHash = CreateLegacyV2Hash(password),
            IsActive = true,
        };
        await _userStore.CreateAsync(user, ct);
        // 解绑种子用户：本测试共用单一 _dbContext，CreateAsync 后该实例仍被跟踪；
        // 若不解绑，LoginAsync 内 AsNoTracking 重新加载的实例在 UpdateAsync 时会与被跟踪实例主键冲突。
        // 生产环境按请求作用域使用全新 DbContext，不存在此问题。
        _dbContext.Entry(user).State = EntityState.Detached;

        var loginResult = await _authService.LoginAsync(new LoginRequest
        {
            Email = email,
            Password = password,
        }, ct);

        // 完成判定（P2-21）：算法升级用户可登录。
        Assert.True(loginResult.Success);
        Assert.NotNull(loginResult.Token);

        // 登录后哈希应被升级为当前 V3 算法，不再等于旧 V2 哈希。
        var updated = await _userStore.GetByEmailAsync(email, ct);
        Assert.NotNull(updated);
        Assert.NotEqual(user.PasswordHash, updated!.PasswordHash);
        Assert.StartsWith("AQ", updated.PasswordHash);
    }

    /// <summary>
    /// 生成旧版（IdentityV2/SHA1）密码哈希，用于模拟算法升级前的存量用户。
    /// </summary>
    private static string CreateLegacyV2Hash(string password)
    {
        var legacyHasher = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2,
        }));
        return legacyHasher.HashPassword(new User(), password);
    }

    [Fact]
    public void PasswordValidator_StrongPassword_ReturnsValid()
    {
        var (isValid, errorMessage) = _passwordValidator.Validate("StrongP@ss1");

        Assert.True(isValid);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void PasswordValidator_TooShort_ReturnsInvalid()
    {
        var (isValid, errorMessage) = _passwordValidator.Validate("Ab1@");

        Assert.False(isValid);
        Assert.Equal("密码长度至少为 8 个字符", errorMessage);
    }

    [Fact]
    public void PasswordValidator_MissingUppercase_ReturnsInvalid()
    {
        var (isValid, errorMessage) = _passwordValidator.Validate("weakpass1@");

        Assert.False(isValid);
        Assert.Equal("密码必须包含至少一个大写字母", errorMessage);
    }

    [Fact]
    public void PasswordValidator_MissingDigit_ReturnsInvalid()
    {
        var (isValid, errorMessage) = _passwordValidator.Validate("NoDigitsHere@");

        Assert.False(isValid);
        Assert.Equal("密码必须包含至少一个数字", errorMessage);
    }

    private sealed class StubTokenService : ITokenService
    {
        public string GenerateAccessToken(Guid userId, string email, IReadOnlyList<string> roles)
            => "stub-token";
    }

    private sealed class StubEventBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
            => Task.CompletedTask;

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
        public bool IsAuthenticated => false;
        public Guid? UserId => null;
        public string? Email => null;
        public IReadOnlyList<string> Roles => [];
    }
}
