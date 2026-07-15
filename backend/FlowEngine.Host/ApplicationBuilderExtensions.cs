using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Identity;
using FlowEngine.Host.Middlewares;
using FlowEngine.Host.Webhooks;
using FlowEngine.Host.WebSocketHandlers;
using FlowEngine.Infrastructure.Audit;
using FlowEngine.Migrations;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.AspNetCore;

namespace FlowEngine.Host;

/// <summary>
/// FlowEngine 应用管道构建扩展方法。
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// 配置 FlowEngine 中间件管道、路由、WebSocket 等。
    /// </summary>
    public static async Task<WebApplication> UseFlowEngineAsync(this WebApplication app)
    {
        // ── Migrations ──────────────────────────────────────────────
        await app.Services.ApplyFlowEngineMigrationsAsync(
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FlowEngine.Migrations"));

        // ── Seed Default Admin ─────────────────────────────────────
        await SeedDefaultAdminAsync(app);

        // ── Startup Initialization ──────────────────────────────────
        await UseInitialization(app);

        // ── Routes ──────────────────────────────────────────────────
        // 注：本方法不调用 app.UseRouting()。所有 Map* 端点（REST API、Controllers、MCP /mcp）
        // 必须在 UseRouting 被调用之前完成注册，因为 ASP.NET Core 在 UseRouting 时会捕获
        // 当前已注册的所有 EndpointDataSource（包括 MapGroup 创建的组路由）。如果先调用
        // UseRouting 再 MapMcp("/mcp")，则 MCP Streamable HTTP 组路由不会被当前路由匹配
        // 管道发现。因此将 UseRoutes 放在 UseMiddlewares（内部调用 UseRouting）之前。
        // 现有 REST/Controller 路由对注册顺序不敏感，但为统一处理并保持兼容性，统一前置。
        UseRoutes(app);

        // ── Middleware ──────────────────────────────────────────────
        UseMiddlewares(app);

        // ── Webhook Routes ──────────────────────────────────────────
        await UseWebhook(app);

        // ── WebSocket ───────────────────────────────────────────────
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30),
        });

        app.Map(RouteConstants.WebSocketPrefix + "/execution", async (HttpContext context) =>
        {
            var handler = context.RequestServices.GetRequiredService<ExecutionWebSocketHandler>();
            await handler.HandleAsync(context,  () => Task.CompletedTask);
        });

        // SPA Fallback：除 API 路由组（RouteConstants.ApiPrefix）外，其余路径回退到 index.html。
        app.MapFallbackToFile(
            $"{{*path:regex(^(?!{RouteConstants.ApiPrefix.TrimStart('/')}(?:/|$)).*$)}}",
            "index.html");

        return app;
    }

    private static Task UseWebhook(WebApplication app)
    {
        // A14：不再于启动期逐条静态映射 Webhook 端点。
        // 改为注册动态路由中间件，请求时按路径实时派发到 IWebhookHandler，
        // 支持运行时新增/删除路由立即生效，无需重启。
        app.UseMiddleware<WebhookRoutingMiddleware>();
        return Task.CompletedTask;
    }

    private static void UseRoutes(WebApplication app)
    {
        app.MapGet(RouteConstants.HealthPrefix, () => Results.Ok(new { status = "healthy" }));
        app.MapGet(RouteConstants.HealthReadyPath, () => Results.Ok(new { status = "ready" }));

        var api = app.MapGroup($"{RouteConstants.ApiPrefix}/v1");
        api.MapGet(RouteConstants.HealthPrefix, () => Results.Ok(new { status = "healthy" }));

        app.MapControllers();

        // ── MCP Streamable HTTP endpoint ────────────────────────────
        app.MapMcp("/mcp").RequireAuthorization();
    }

    private static void UseMiddlewares(WebApplication app)
    {
        // 在读取客户端 IP 之前应用转发头（反向代理场景），防止 X-Forwarded-For 伪造绕过基于 IP 的限流（L2）。
        app.UseForwardedHeaders();
        // 全局异常处理放在管道最外层，确保能捕获后续所有中间件与端点抛出的异常（GAP-26）
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        // 显式启用路由匹配，确保后续中间件可读取路由值（GAP-12）
        app.UseRouting();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        // L7：SPA 静态资源（index.html、js、css）匿名可访问，置于认证/授权前，
        // 避免登录页等公共资源被强制要求先登录；安全响应头仍作用于静态响应。
        app.UseStaticFiles();
        app.UseMiddleware<RateLimitMiddleware>();
        app.UseCors();
        app.UseAuthentication();
        app.UseMiddleware<CurrentUserMiddleware>();
        app.UseAuthorization();
        app.UseMiddleware<RbacAuthorizationMiddleware>();
    }

    private static async Task UseInitialization(WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var scheduleManager = scope.ServiceProvider.GetRequiredService<IScheduleManager>();

        // Quartz 托管服务（AddQuartzHostedService）会在应用启动时自动启动调度器，
        // 此处不再重复调用 StartAsync，避免双重启动与生命周期边界混乱。
        var activeTriggers = await dbContext.Triggers.Where(t => t.IsActive).ToListAsync();
        foreach (var trigger in activeTriggers)
        {
            if (trigger.Type != TriggerType.Schedule) continue;

            var settings = trigger.Settings;
            if (settings?.CronExpression is null) continue;

            await scheduleManager.RegisterScheduleAsync(
                trigger.Id,
                trigger.WorkflowDefinitionId,
                settings.CronExpression,
                settings.TimeZone,
                settings.StartAt,
                settings.EndAt);
        }

        // 应用重启后恢复激活的 Poll 触发器调度（GAP-18）
        foreach (var trigger in activeTriggers)
        {
            if (trigger.Type != TriggerType.Poll) continue;

            var settings = trigger.Settings;
            if (settings is null) continue;

            await scheduleManager.RegisterPollTriggerAsync(
                trigger.Id,
                trigger.WorkflowDefinitionId,
                settings.IntervalSeconds);
        }
    }

    private static async Task SeedDefaultAdminAsync(WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        if (await dbContext.Set<User>().AnyAsync())
        {
            return;
        }

        var password = ResolveDefaultAdminPassword(app);

        var admin = new User
        {
            Email = "admin@flowengine.local",
            UserName = "admin",
            DisplayName = "Administrator",
            PasswordHash = passwordHasher.HashPassword(password),
            IsActive = true,
        };

        dbContext.Set<User>().Add(admin);
        await dbContext.SaveChangesAsync();

        // 为默认 admin 分配 Admin 角色，确保首次部署后可访问受保护端点（GAP-04）。
        dbContext.Set<UserRole>().Add(new UserRole
        {
            UserId = admin.Id,
            Role = FlowEngine.Core.Authorization.Role.Admin.ToString()
        });
        await dbContext.SaveChangesAsync();
    }

    private static string ResolveDefaultAdminPassword(WebApplication app)
    {
        const string EnvVarName = "FLOWENGINE_ADMIN_PASSWORD";
        var password = app.Configuration["Setup:AdminPassword"]
            ?? Environment.GetEnvironmentVariable(EnvVarName);

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                $"首次启动必须设置管理员密码。请设置配置项 Setup:AdminPassword 或环境变量 {EnvVarName} 后重新启动。");
        }

        // 管理员密码要求至少 12 位，高于普通用户密码策略。
        var passwordValidator = new PasswordValidator(minLength: 12);
        var (isValid, errorMessage) = passwordValidator.Validate(password);
        if (!isValid)
        {
            throw new InvalidOperationException(
                $"管理员密码不符合强度要求：{errorMessage}");
        }

        return password;
    }
}
