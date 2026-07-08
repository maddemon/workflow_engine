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
using Microsoft.EntityFrameworkCore;

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

        // ── Middleware ──────────────────────────────────────────────
        UseMiddlewares(app);

        // ── Routes ──────────────────────────────────────────────────
        UseRoutes(app);

        // ── Webhook Routes ──────────────────────────────────────────
        await UseWebhook(app);

        // ── WebSocket ───────────────────────────────────────────────
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30),
        });

        app.Map("/ws/execution", async (HttpContext context) =>
        {
            var handler = context.RequestServices.GetRequiredService<ExecutionWebSocketHandler>();
            await handler.HandleAsync(context,  () => Task.CompletedTask);
        });

        app.MapFallbackToFile("{*path:regex(^(?!api(?:/|$)).*$)}", "index.html");

        return app;
    }

    private static async Task UseWebhook(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FlowEngine.Webhook");
        var webhookRoutes = await dbContext.WebhookRoutes.AsNoTracking().ToListAsync();

        var reservedPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/api/",
            "/health",
            "/ws/",
        };
        var registeredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var route in webhookRoutes)
        {
            var capturedPath = route.Path;
            if (string.IsNullOrWhiteSpace(capturedPath) || !capturedPath.StartsWith('/'))
            {
                logger.LogWarning("Webhook 路由路径不合法，已跳过注册。RouteId={RouteId}, Path={Path}", route.Id, capturedPath);
                continue;
            }

            if (reservedPrefixes.Any(prefix => capturedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                logger.LogWarning("Webhook 路由路径与保留前缀冲突，已跳过注册。RouteId={RouteId}, Path={Path}", route.Id, capturedPath);
                continue;
            }

            if (!registeredPaths.Add(capturedPath))
            {
                logger.LogWarning("Webhook 路由路径重复，已跳过注册。RouteId={RouteId}, Path={Path}", route.Id, capturedPath);
                continue;
            }

            var method = route.Method?.ToUpperInvariant() ?? "POST";

            app.MapMethods(capturedPath, new[] { method }, async (HttpContext context) =>
            {
                var handler = context.RequestServices.GetRequiredService<WebhookHandler>();
                await handler.HandleAsync(context, capturedPath);
            })
            .WithName($"webhook_{route.Id}")
            .WithMetadata(new WebhookEndpointMetadata(route.Id));
        }
    }

    private static void UseRoutes(WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
        app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));

        var api = app.MapGroup("/api/v1");
        api.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

        app.MapControllers();
    }

    private static void UseMiddlewares(WebApplication app)
    {
        // 全局异常处理放在管道最外层，确保能捕获后续所有中间件与端点抛出的异常（GAP-26）
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        // 显式启用路由匹配，确保后续中间件可读取路由值（GAP-12）
        app.UseRouting();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<RateLimitMiddleware>();
        app.UseCors();
        app.UseAuthentication();
        app.UseMiddleware<CurrentUserMiddleware>();
        app.UseAuthorization();
        app.UseMiddleware<RbacAuthorizationMiddleware>();
        app.UseStaticFiles();
    }

    private static async Task UseInitialization(WebApplication app)
    {
        app.Services.GetRequiredService<AuditLogFileSink>();

        using var scope = app.Services.CreateScope();
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
        using var scope = app.Services.CreateScope();
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
