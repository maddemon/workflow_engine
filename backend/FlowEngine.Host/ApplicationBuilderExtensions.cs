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
        var dbProvider = app.Configuration["Database:Provider"] ?? "sqlite";
        await app.Services.ApplyFlowEngineMigrationsAsync(
            dbProvider,
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
            await handler.HandleAsync(context, async () => { });
        });

        app.MapFallbackToFile("index.html");

        return app;
    }

    private static async Task UseWebhook(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var webhookRoutes = await dbContext.WebhookRoutes.ToListAsync();

        foreach (var route in webhookRoutes)
        {
            var capturedPath = route.Path;
            var method = route.Method?.ToUpperInvariant() ?? "POST";

            app.MapMethods(capturedPath, new[] { method }, async (HttpContext context) =>
            {
                var handler = context.RequestServices.GetRequiredService<WebhookHandler>();
                await handler.HandleAsync(context, capturedPath);
            })
            .WithName($"webhook_{route.Id}")
            .WithMetadata(new { IsWebhook = true });
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
        app.UseCors();
        app.UseAuthentication();
        app.UseMiddleware<CurrentUserMiddleware>();
        app.UseAuthorization();
        app.UseStaticFiles();
    }

    private static async Task UseInitialization(WebApplication app)
    {
        app.Services.GetRequiredService<AuditLogFileSink>();

        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        var scheduleManager = scope.ServiceProvider.GetRequiredService<IScheduleManager>();

        await scheduleManager.StartAsync();

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

        var admin = new User
        {
            Email = "admin@flowengine.local",
            UserName = "admin",
            DisplayName = "Administrator",
            PasswordHash = passwordHasher.HashPassword("admin123"),
            IsActive = true,
        };

        dbContext.Set<User>().Add(admin);
        await dbContext.SaveChangesAsync();
    }
}
