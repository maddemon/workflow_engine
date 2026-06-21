using System.Text;
using System.Text.Json.Serialization;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Credentials;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Executions;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Triggers;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Host.Executor;
using FlowEngine.Host.Middlewares;
using FlowEngine.Host.Scheduling;
using FlowEngine.Host.Webhooks;
using FlowEngine.Host.WebSocketHandlers;
using FlowEngine.Infrastructure.Audit;
using FlowEngine.Infrastructure.Identity;
using FlowEngine.Infrastructure.Security;
using FlowEngine.Migrations;
using FlowEngine.Runtime.Credentials;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Registry;
using FlowEngine.Runtime.Scripting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MySql.EntityFrameworkCore.Extensions;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

// ── Controllers & JSON ────────────────────────────────────────────

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddMemoryCache();

// ── Database ──────────────────────────────────────────────────────

var dbProvider = builder.Configuration["Database:Provider"] ?? "sqlite";
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

builder.Services.AddDbContext<FlowEngineDbContext>(options =>
{
    switch (dbProvider.ToLowerInvariant())
    {
        case "sqlite":
            options.UseSqlite(connectionString, x =>
                x.MigrationsAssembly("FlowEngine.Migrations")
                 .MigrationsHistoryTable("__ef_migrations_history"));
            break;
        case "postgresql" or "npgsql" or "kingbasees" or "kingbase":
            options.UseNpgsql(connectionString, x =>
                x.MigrationsAssembly("FlowEngine.Migrations")
                 .MigrationsHistoryTable("__ef_migrations_history", "flow"));
            break;
        case "mysql" or "pomelo" or "tidb" or "oceanbase":
            options.UseMySQL(connectionString, x =>
                x.MigrationsAssembly("FlowEngine.Migrations")
                 .MigrationsHistoryTable("__ef_migrations_history"));
            break;
        case "dameng" or "dm":
            options.UseDm(connectionString, x =>
                x.MigrationsAssembly("FlowEngine.Migrations")
                 .MigrationsHistoryTable("__ef_migrations_history"));
            break;
        default:
            throw new ArgumentException($"Unsupported database provider: {dbProvider}");
    }
});

// ── Infrastructure ────────────────────────────────────────────────

builder.Services.AddSingleton<InternalErrorSink>();
builder.Services.AddSingleton<IEventBus, InMemoryEventBus>();
builder.Services.AddScoped<ParameterResolver>();
builder.Services.AddScoped<CredentialAccessor>();

builder.Services.AddSingleton<AuditLogFileSink>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var logPath = config["Audit:LogPath"] ?? "./storage/audit";
    return new AuditLogFileSink(
        logPath,
        sp.GetRequiredService<IEventBus>(),
        sp.GetService<ILogger<AuditLogFileSink>>());
});
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var logPath = config["Audit:LogPath"] ?? "./storage/audit";
    return new AuditLogReader(logPath);
});

// ── Authentication & Authorization ────────────────────────────────

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("JWT Secret is not configured.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "FlowEngine",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "FlowEngine",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

// ── Identity ──────────────────────────────────────────────────────

builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IPasswordValidator, PasswordValidator>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IUserStore, UserStore>();
builder.Services.AddScoped<IUserContext, HttpContextUserContext>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<AuditEventFactory>();

// ── Business ──────────────────────────────────────────────────────

builder.Services.AddSingleton<ICryptoKeyProvider, CryptoKeyProvider>();
builder.Services.AddSingleton<ICredentialEncryptionService, CredentialEncryptionService>();
builder.Services.AddScoped<CredentialService>();
builder.Services.AddScoped<ICredentialAccessor, CredentialAccessor>();
builder.Services.AddScoped<WorkflowValidator>();
builder.Services.AddScoped<WorkflowService>();
builder.Services.AddScoped<TriggerService>();
builder.Services.AddScoped<WebhookHandler>();
builder.Services.AddScoped<ErrorStrategyHandler>();
builder.Services.AddSingleton<WorkflowExecutionQueue>();
builder.Services.AddScoped<IEngine, WorkflowExecutor>();
builder.Services.AddHostedService<WorkflowExecutionWorker>();

// ── Scheduling & Execution ────────────────────────────────────────

builder.Services.AddSingleton<IScheduleManager, QuartzScheduleManager>();
builder.Services.AddQuartz();
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

builder.Services.AddScoped<ExecutionService>();
builder.Services.AddScoped<NodeExecutionContextFactory>(provider =>
{
    var whitelist = builder.Configuration.GetSection("Expression:EnvironmentWhitelist").Get<string[]>() ?? [];
    return new NodeExecutionContextFactory(
        provider.GetRequiredService<INodeRegistry>(),
        provider.GetRequiredService<ParameterResolver>(),
        provider.GetRequiredService<ICredentialAccessor>(),
        new HashSet<string>(whitelist, StringComparer.OrdinalIgnoreCase),
        hydratorLogger: provider.GetService<ILogger<ParameterHydrator>>(),
        jsLogger: provider.GetService<ILogger<JsEngine>>());
});

// ── WebSocket ─────────────────────────────────────────────────────

builder.Services.AddSingleton<WebSocketConnectionManager>();
builder.Services.AddSingleton<WebSocketEventPushService>();
builder.Services.AddSingleton<WebSocketReplayService>();
builder.Services.AddScoped<ExecutionWebSocketHandler>();

// ── CORS ──────────────────────────────────────────────────────────

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length > 0)
        {
            policy.WithOrigins(origins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});

// ── Plugins & Node Registry ───────────────────────────────────────

var pluginsPath = builder.Configuration.GetSection("Plugins")["Path"] ?? "../../plugins";
if (!Path.IsPathRooted(pluginsPath))
{
    pluginsPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, pluginsPath));
}

builder.Services.AddSingleton<PluginLoader>(_ =>
    new PluginLoader(pluginsPath, _.GetRequiredService<ILogger<PluginLoader>>()));

builder.Services.AddSingleton<INodeRegistry>(provider =>
{
    var loader = provider.GetRequiredService<PluginLoader>();
    var nodes = loader.LoadNodes();
    var logger = provider.GetRequiredService<ILogger<NodeRegistry>>();
    var registry = new NodeRegistry(nodes, logger);

    logger.LogInformation(
        "节点注册中心初始化完成，已注册 {Count} 个节点类型。",
        registry.GetDescriptors().Count);

    return registry;
});

// ══════════════════════════════════════════════════════════════════
//  App Pipeline
// ══════════════════════════════════════════════════════════════════

var app = builder.Build();

// ── Migrations ────────────────────────────────────────────────────

await app.Services.ApplyFlowEngineMigrationsAsync(
    dbProvider,
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FlowEngine.Migrations"));

// ── Startup Initialization ────────────────────────────────────────

app.Services.GetRequiredService<AuditLogFileSink>();

using (var scope = app.Services.CreateScope())
{
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

// ── Middleware ────────────────────────────────────────────────────

app.UseCors();
app.UseAuthentication();
app.UseMiddleware<CurrentUserMiddleware>();
app.UseAuthorization();
app.UseStaticFiles();

// ── Routes ────────────────────────────────────────────────────────

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));

var api = app.MapGroup("/api/v1");
api.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapControllers();

// ── Webhook Routes ────────────────────────────────────────────────

using (var scope = app.Services.CreateScope())
{
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

// ── WebSocket ─────────────────────────────────────────────────────

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

app.Run();
