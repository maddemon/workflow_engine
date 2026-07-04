using FlowEngine.Application.Authorization;
using Microsoft.AspNetCore.Http;

namespace FlowEngine.Host.Middlewares;

/// <summary>
/// 项目上下文中间件，从路由参数或 query string 解析 projectId 并注入到 <see cref="IProjectContext"/>（GAP-12）。
/// </summary>
public class ProjectContextMiddleware(RequestDelegate next)
{
    /// <summary>
    /// 处理请求：解析 projectId 并设置到 IProjectContext.CurrentProjectId，无 projectId 时保持为 null 不阻塞请求。
    /// </summary>
    public async Task InvokeAsync(HttpContext context, IProjectContext projectContext)
    {
        Guid? projectId = null;

        // 优先从路由参数解析（如 /api/projects/{projectId}/...）
        var routeValue = context.GetRouteValue("projectId")?.ToString();
        if (!string.IsNullOrWhiteSpace(routeValue) && Guid.TryParse(routeValue, out var parsedFromRoute))
        {
            projectId = parsedFromRoute;
        }
        else
        {
            // 回退到 query string
            var queryValue = context.Request.Query["projectId"].ToString();
            if (!string.IsNullOrWhiteSpace(queryValue) && Guid.TryParse(queryValue, out var parsedFromQuery))
            {
                projectId = parsedFromQuery;
            }
        }

        projectContext.CurrentProjectId = projectId;

        await next(context);
    }
}
