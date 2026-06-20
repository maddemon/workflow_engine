using FlowEngine.Application.Audit;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

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
    AuditEventFactory auditFactory) : ControllerBase
{
    /// <summary>
    /// 用户注册。
    /// </summary>
    [HttpPost("register")]
    public async Task<ActionResult<RegisterResult>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authenticationService.RegisterAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            return result.ErrorMessage switch
            {
                RegisterResultErrors.EmailAlreadyExists => Conflict(result),
                _ => BadRequest(result),
            };
        }

        return StatusCode(StatusCodes.Status201Created, result);
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
    /// 当前使用无状态 JWT，登出由客户端丢弃 Token 实现。
    /// 后续可引入 Token 黑名单使已签发 Token 提前失效。
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

        // TODO: Token 黑名单 (Beta 阶段引入 Refresh Token 后实现)
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
