using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Identity;
using FlowEngine.Host.Middlewares;
using FlowEngine.Host.Observability;
using FlowEngine.Host.Webhooks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using FlowEngine.Host.WebSocketHandlers;
using FlowEngine.Infrastructure.Audit;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.AspNetCore;
using System.Globalization;

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

        // ── Webhook Routes ──────────────────────────────────────────
        // 必须在认证/授权中间件之前注册：Webhook 以 HMAC 签名鉴权，无用户身份，
        // 若晚于 FallbackPolicy（RequireAuthenticatedUser）注册会被误判为匿名而拒绝。
        // 该中间件对 Webhook POST 直接短路响应，非 Webhook 请求则透传至后续管道。
        await UseWebhook(app);

        // ── Middleware ──────────────────────────────────────────────
        UseMiddlewares(app);

        // ── WebSocket ───────────────────────────────────────────────
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30),
        });

        app.Map(RouteConstants.WebSocketPrefix + "/execution", async (HttpContext context) =>
        {
            var handler = context.RequestServices.GetRequiredService<ExecutionWebSocketHandler>();
            await handler.HandleAsync(context,  () => Task.CompletedTask);
        }).AllowAnonymous();

        // SPA Fallback：除 API 路由组（RouteConstants.ApiPrefix）外，其余路径回退到 index.html。
        // SEC-2：登录页等公共资源需匿名可访问，显式放行（全局 FallbackPolicy 默认要求认证）。
        app.MapFallbackToFile(
            $"{{*path:regex(^(?!{RouteConstants.ApiPrefix.TrimStart('/')}(?:/|$)).*$)}}",
            "index.html").AllowAnonymous();

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
        app.MapControllers();

        // ── MCP Streamable HTTP endpoint ────────────────────────────
        app.MapMcp("/mcp").RequireAuthorization();
    }

    private static void UseMiddlewares(WebApplication app)
    {
        // 在读取客户端 IP 之前应用转发头（反向代理场景），防止 X-Forwarded-For 伪造绕过基于 IP 的限流（L2）。
        app.UseForwardedHeaders();
        // E-4：请求访问日志（方法/路径/状态码/耗时），健康检查端点已在中间件内排除。
        app.UseMiddleware<RequestLoggingMiddleware>();
        // 全局异常处理放在管道最外层，确保能捕获后续所有中间件与端点抛出的异常（GAP-26）
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        // O-6：健康检查端点（liveness 存活探针 / readiness 就绪探针含数据库探测），
        // 在路由与认证之前短路返回，避免被 FallbackPolicy 要求认证。
        app.UseHealthChecks(RouteConstants.HealthPrefix, LiveHealthOptions);
        app.UseHealthChecks(RouteConstants.HealthReadyPath, ReadyHealthOptions);
        app.UseHealthChecks($"{RouteConstants.ApiPrefix}/v1{RouteConstants.HealthPrefix}", LiveHealthOptions);
        // 显式启用路由匹配，确保后续中间件可读取路由值（GAP-12）
        app.UseRouting();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        // L7：SPA 静态资源（index.html、js、css）匿名可访问，置于认证/授权前，
        // 避免登录页等公共资源被强制要求先登录；安全响应头仍作用于静态响应。
        app.UseStaticFiles();
        // 限流（任务 2.3）：基于 System.Threading.RateLimiting 的全局分区限流器，
        // 按路径分类 Login/Register/Api 并对每客户端独立限流，白名单/禁用规则跳过。
        // 置于认证/授权之前，使登录/注册等匿名端点同样受限，与原手搓中间件位置一致。
        app.UseRateLimiter();
        app.UseCors();
        app.UseAuthentication();
        // S-4：Cookie 认证 CSRF 防护。仅对携带 fe_auth Cookie 的变更请求要求自定义防伪造头，
        // Bearer/API Key 与匿名请求不受影响。置于认证之后，确保 Cookie 已就绪。
        app.UseMiddleware<CsrfProtectionMiddleware>();
        app.UseMiddleware<CurrentUserMiddleware>();
        app.UseAuthorization();

        var supportedCultures = new[]
        {
            new CultureInfo("en"),
            new CultureInfo("zh-CN"),
        };

        app.UseRequestLocalization(new RequestLocalizationOptions
        {
            DefaultRequestCulture = new("en"),
            SupportedCultures = supportedCultures,
            SupportedUICultures = supportedCultures,
        });

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

    // O-6：liveness 探针仅包含存活检查（不依赖数据库等外部依赖）。
    private static readonly HealthCheckOptions LiveHealthOptions = new()
    {
        Predicate = registration => registration.Tags.Contains(ObservabilityExtensions.LivenessTag),
        ResponseWriter = WriteHealthResponse,
    };

    // O-6：readiness 探针包含数据库连通性探测（tag "ready"）。
    private static readonly HealthCheckOptions ReadyHealthOptions = new()
    {
        Predicate = registration => registration.Tags.Contains(ObservabilityExtensions.ReadinessTag),
        ResponseWriter = WriteHealthResponse,
    };

    /// <summary>
    /// 将健康报告序列化为统一 JSON 响应，保持 <c>{ "status": "healthy" | "unhealthy" }</c> 形状（兼容旧静态桩）。
    /// </summary>
    private static Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var status = report.Status.ToString().ToLowerInvariant();
        return context.Response.WriteAsJsonAsync(new { status });
    }
}
