using System.Security.Claims;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Events;

namespace FlowEngine.Host.Middlewares;

/// <summary>
/// RBAC 授权中间件，检查 <see cref="AuthorizePermissionAttribute"/> 标注的端点权限。
/// </summary>
public class RbacAuthorizationMiddleware(RequestDelegate next)
{
    /// <summary>
    /// 处理请求：若端点带有 <see cref="AuthorizePermissionAttribute"/>，则验证用户角色权限。
    /// </summary>
    public async Task InvokeAsync(
        HttpContext context,
        AuthorizationService authorizationService,
        IEventBus eventBus,
        AuditEventFactory auditFactory)
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
                // 审计：权限拒绝时记录审计事件。
                await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                    AuditEventTypes.PermissionDenied,
                    attribute.Scope.ToString(),
                    Guid.Empty,
                    new Dictionary<string, object>
                    {
                        ["scope"] = attribute.Scope.ToString(),
                        ["operation"] = attribute.Operation.ToString(),
                        ["path"] = context.Request.Path.Value ?? string.Empty,
                    }),
                    context.RequestAborted).ConfigureAwait(false);

                // 返回与全局异常中间件一致的统一错误包络 { success, errorCode, message, details }。
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    errorCode = "Forbidden",
                    message = $"Insufficient permissions: {attribute.Scope}:{attribute.Operation} required.",
                    details = (object?)null,
                }).ConfigureAwait(false);
                return;
            }
        }

        await next(context);
    }
}
