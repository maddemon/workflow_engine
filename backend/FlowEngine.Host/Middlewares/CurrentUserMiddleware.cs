using System.Security.Claims;
using FlowEngine.Application.Identity;
using Microsoft.AspNetCore.Http;

namespace FlowEngine.Host.Middlewares;

/// <summary>
/// 当前用户中间件，将 JWT Claims 中的用户标识映射到标准 ClaimTypes，
/// 确保 IUserContext 能正确解析用户信息。
/// </summary>
public class CurrentUserMiddleware(RequestDelegate next)
{
    /// <summary>
    /// 处理请求：将 JWT 的 sub claim 映射为 ClaimTypes.NameIdentifier。
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var sub = user.FindFirstValue("sub");
            if (sub is not null && user.FindFirstValue(ClaimTypes.NameIdentifier) is null)
            {
                var identity = new ClaimsIdentity(user.Identity);
                identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, sub));
                context.User = new ClaimsPrincipal(identity);
            }
        }

        await next(context);
    }
}
