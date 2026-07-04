using System.IdentityModel.Tokens.Jwt;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// 认证 API。
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController(
    AuthenticationService authenticationService,
    IUserContext userContext,
    IUserStore userStore,
    IEventBus eventBus,
    AuditEventFactory auditFactory,
    ITokenBlacklist tokenBlacklist,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>
    /// 用户注册（已关闭）。本系统为内部私有化部署，账号由管理员或 SSO 统一创建，不提供自助注册。
    /// </summary>
    [HttpPost("register")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public ActionResult Register([FromBody] RegisterRequest request)
    {
        return StatusCode(StatusCodes.Status403Forbidden, new
        {
            error = "Forbidden",
            message = "自助注册已关闭，请联系管理员创建账号。"
        });
    }

    /// <summary>
    /// 用户登录，返回 JWT 令牌。
    /// </summary>
    [HttpPost("login")]
    public async Task<ActionResult<LoginResult>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authenticationService.LoginAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            return Unauthorized(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// 用户登出。
    /// </summary>
    /// <remarks>
    /// 将当前请求的 JWT 加入内存黑名单，使该 Token 在有效期内提前失效。
    /// </remarks>
    [Authorize]
    [HttpPost("logout")]
    public async Task<ActionResult> Logout(CancellationToken cancellationToken)
    {
        if (userContext.UserId is not null)
        {
            await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                AuditEventTypes.UserLogout,
                "User",
                userContext.UserId.Value),
                cancellationToken).ConfigureAwait(false);
        }

        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..];
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(token);
                await tokenBlacklist.AddAsync(jwt.Id, jwt.ValidTo, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to invalidate token during logout");
            }
        }

        return Ok();
    }

    /// <summary>
    /// 获取当前登录用户信息。
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> GetCurrentUser(CancellationToken cancellationToken)
    {
        if (userContext.UserId is null)
        {
            return Unauthorized();
        }

        var user = await userStore.GetByIdAsync(userContext.UserId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return NotFound();
        }

        return Ok(new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            UserName = user.UserName,
            DisplayName = user.DisplayName,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
        });
    }
}
