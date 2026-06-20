using FlowEngine.Application.Identity;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace FlowEngine.Host.Middlewares;

/// <summary>
/// 基于 HttpContext 的当前用户上下文实现。
/// 从 JWT 认证后的 Claims 中提取用户信息。
/// </summary>
public class HttpContextUserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
    /// <inheritdoc />
    public bool IsAuthenticated =>
        httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public Guid? UserId
    {
        get
        {
            var sub = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }

    /// <inheritdoc />
    public string? Email =>
        httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Email);

    /// <inheritdoc />
    public IReadOnlyList<string> Roles =>
        httpContextAccessor.HttpContext?.User?.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList() ?? [];
}
