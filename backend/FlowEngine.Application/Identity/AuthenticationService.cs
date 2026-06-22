using System.Text.RegularExpressions;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Dtos;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Events;
using FlowEngine.Core.Identity;

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
    AuditEventFactory auditFactory)
{
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

        var user = await userStore.GetByEmailAsync(request.Email, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return new LoginResult
            {
                Success = false,
                ErrorMessage = "邮箱或密码错误",
            };
        }

        if (!passwordHasher.VerifyPassword(user.PasswordHash, request.Password))
        {
            return new LoginResult
            {
                Success = false,
                ErrorMessage = "邮箱或密码错误",
            };
        }

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

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();
}
