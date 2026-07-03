using System.Text.RegularExpressions;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Events;
using FlowEngine.Core.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace FlowEngine.Application.Identity;

/// <summary>
/// 认证服务，处理注册和登录业务逻辑。
/// </summary>
public partial class AuthenticationService(
    IUserStore userStore,
    IPasswordHasher passwordHasher,
    IPasswordValidator passwordValidator,
    ITokenService tokenService,
    IEventBus eventBus,
    AuditEventFactory auditFactory,
    IMemoryCache? memoryCache = null)
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// 用户注册。
    /// </summary>
    /// <param name="request">注册请求。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>注册结果。</returns>
    public async Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !EmailRegex().IsMatch(request.Email))
        {
            return new RegisterResult
            {
                Success = false,
                ErrorMessage = "邮箱格式无效",
            };
        }

        var existingUser = await userStore.GetByEmailAsync(request.Email, ct).ConfigureAwait(false);
        if (existingUser is not null)
        {
            return new RegisterResult
            {
                Success = false,
                ErrorMessage = RegisterResultErrors.EmailAlreadyExists,
            };
        }

        var (isValid, errorMessage) = passwordValidator.Validate(request.Password);
        if (!isValid)
        {
            return new RegisterResult
            {
                Success = false,
                ErrorMessage = errorMessage,
            };
        }

        var passwordHash = passwordHasher.HashPassword(request.Password);

        var user = new User
        {
            Email = request.Email,
            UserName = request.UserName,
            DisplayName = request.DisplayName ?? request.UserName,
            PasswordHash = passwordHash,
        };

        var created = await userStore.CreateAsync(user, ct).ConfigureAwait(false);

        await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
            AuditEventTypes.UserRegistered,
            "User",
            created.Id,
            new Dictionary<string, object> { ["email"] = created.Email }),
            ct).ConfigureAwait(false);

        return new RegisterResult
        {
            Success = true,
            UserId = created.Id,
        };
    }

    /// <summary>
    /// 用户登录。
    /// </summary>
    /// <param name="request">登录请求。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>登录结果（含 JWT 令牌）。</returns>
    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return new LoginResult
            {
                Success = false,
                ErrorMessage = "邮箱和密码不能为空",
            };
        }

        if (IsLockedOut(request.Email))
        {
            return new LoginResult
            {
                Success = false,
                ErrorMessage = $"登录尝试过多，请 {LockoutDuration.TotalMinutes} 分钟后再试",
            };
        }

        var user = await userStore.GetByEmailAsync(request.Email, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            RecordFailedAttempt(request.Email);
            return new LoginResult
            {
                Success = false,
                ErrorMessage = "邮箱或密码错误",
            };
        }

        if (!passwordHasher.VerifyPassword(user.PasswordHash, request.Password))
        {
            RecordFailedAttempt(request.Email);
            return new LoginResult
            {
                Success = false,
                ErrorMessage = "邮箱或密码错误",
            };
        }

        ClearFailedAttempts(request.Email);

        var roles = await userStore.GetRolesAsync(user.Id, ct).ConfigureAwait(false);
        var roleNames = roles.Select(r => r.Role).ToList();

        var token = tokenService.GenerateAccessToken(user.Id, user.Email, roleNames);

        await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
            AuditEventTypes.UserLogin,
            "User",
            user.Id,
            new Dictionary<string, object> { ["email"] = user.Email }),
            ct).ConfigureAwait(false);

        return new LoginResult
        {
            Success = true,
            Token = token,
            UserId = user.Id,
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                UserName = user.UserName,
                DisplayName = user.DisplayName,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt,
            },
        };
    }

    private string GetAttemptCacheKey(string email) => $"login-attempts:{email.ToLowerInvariant()}";

    private bool IsLockedOut(string email)
    {
        if (memoryCache is null)
        {
            return false;
        }

        var key = GetAttemptCacheKey(email);
        if (!memoryCache.TryGetValue(key, out LoginAttemptState? state) || state is null)
        {
            return false;
        }

        if (state.LockoutUntil.HasValue && state.LockoutUntil.Value > DateTime.UtcNow)
        {
            return true;
        }

        return false;
    }

    private void RecordFailedAttempt(string email)
    {
        if (memoryCache is null)
        {
            return;
        }

        var key = GetAttemptCacheKey(email);
        var state = memoryCache.TryGetValue(key, out LoginAttemptState? existing) && existing is not null
            ? existing
            : new LoginAttemptState();

        var now = DateTime.UtcNow;
        if (state.FirstAttempt.HasValue && now - state.FirstAttempt.Value > AttemptWindow)
        {
            state = new LoginAttemptState { FirstAttempt = now, FailedAttempts = 1 };
        }
        else
        {
            state.FirstAttempt ??= now;
            state.FailedAttempts++;
        }

        if (state.FailedAttempts >= MaxFailedAttempts)
        {
            state.LockoutUntil = now + LockoutDuration;
        }

        memoryCache.Set(key, state, LockoutDuration);
    }

    private void ClearFailedAttempts(string email)
    {
        memoryCache?.Remove(GetAttemptCacheKey(email));
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    private sealed class LoginAttemptState
    {
        public int FailedAttempts { get; set; }
        public DateTime? FirstAttempt { get; set; }
        public DateTime? LockoutUntil { get; set; }
    }
}
