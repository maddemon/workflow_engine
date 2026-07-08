using System.IdentityModel.Tokens.Jwt;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Events;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
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
    ApiKeyService apiKeyService,
    IUserContext userContext,
    IUserStore userStore,
    IEventBus eventBus,
    AuditEventFactory auditFactory,
    ITokenBlacklist tokenBlacklist,
    IWebHostEnvironment environment,
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
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await authenticationService.LoginAsync(request, cancellationToken, clientIp)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            return Unauthorized(result);
        }

        // L1/H5：登录成功后下发 HttpOnly Cookie 承载 JWT，前端不再经 URL/JS 暴露令牌。
        var token = result.Token ?? throw new InvalidOperationException("登录成功但未生成令牌。");
        Response.Cookies.Append("fe_auth", token, new CookieOptions
        {
            HttpOnly = true,
            // 本地开发为 http，Secure Cookie 不会被发送；仅在生产（https）启用 Secure。
            Secure = !environment.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Expires = DateTime.UtcNow.AddDays(7),
        });

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

        // L1/H5：登出时清除 HttpOnly Cookie，使前端不再携带已失效令牌。
        if (Request.Cookies.ContainsKey("fe_auth"))
        {
            Response.Cookies.Delete("fe_auth");
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

        var roles = await userStore.GetRolesAsync(user.Id, cancellationToken)
            .ConfigureAwait(false);

        return Ok(new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            UserName = user.UserName,
            DisplayName = user.DisplayName,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            Roles = roles.Select(r => r.Role).ToList(),
        });
    }

    /// <summary>
    /// 创建 API Key（Personal Access Token）。
    /// </summary>
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api-keys")]
    public async Task<ActionResult<CreateApiKeyResult>> CreateApiKey(
        [FromBody] CreateApiKeyRequest request,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Name is required." });
        }

        var result = await apiKeyService.CreateAsync(
            userContext.UserId.Value,
            request.Name.Trim(),
            request.ExpiresAt,
            cancellationToken).ConfigureAwait(false);

        return Ok(result);
    }

    /// <summary>
    /// 列出当前用户的 API Key。
    /// </summary>
    [Authorize]
    [HttpGet("api-keys")]
    public async Task<ActionResult<IReadOnlyList<ApiKeyDto>>> ListApiKeys(CancellationToken cancellationToken)
    {
        if (userContext.UserId is null)
        {
            return Unauthorized();
        }

        var list = await apiKeyService.ListAsync(userContext.UserId.Value, cancellationToken)
            .ConfigureAwait(false);

        return Ok(list);
    }

    /// <summary>
    /// 吊销指定 API Key。
    /// </summary>
    [Authorize]
    [HttpDelete("api-keys/{id:guid}")]
    public async Task<ActionResult> RevokeApiKey(Guid id, CancellationToken cancellationToken)
    {
        if (userContext.UserId is null)
        {
            return Unauthorized();
        }

        var result = await apiKeyService.RevokeAsync(userContext.UserId.Value, id, cancellationToken)
            .ConfigureAwait(false);

        if (!result)
        {
            return NotFound();
        }

        return NoContent();
    }
}
