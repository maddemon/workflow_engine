using System.Security.Claims;
using FlowEngine.Application.Authorization;
using FlowEngine.Core.Authorization;

namespace FlowEngine.Host.Middlewares;

/// <summary>
/// RBAC 授权中间件，检查 <see cref="AuthorizePermissionAttribute"/> 标注的端点权限。
/// </summary>
public class RbacAuthorizationMiddleware(RequestDelegate next)
{
    /// <summary>
    /// 处理请求：若端点带有 <see cref="AuthorizePermissionAttribute"/>，则验证用户角色权限。
    /// </summary>
    public async Task InvokeAsync(HttpContext context, AuthorizationService authorizationService)
    {
        var endpoint = context.GetEndpoint();
        var attribute = endpoint?.Metadata.GetMetadata<AuthorizePermissionAttribute>();

        if (attribute is not null)
        {
            var roles = context.User.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();

            if (!authorizationService.HasPermission(roles, attribute.Scope, attribute.Operation))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Forbidden",
                    message = $"Insufficient permissions: {attribute.Scope}:{attribute.Operation} required."
                });
                return;
            }
        }

        await next(context);
    }
}
