using FlowEngine.Application.Audit;
using FlowEngine.Application.Credentials;
using FlowEngine.Application.Executions;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Triggers;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Events;
using FlowEngine.Host.Executor;
using FlowEngine.Host.Middlewares;
using FlowEngine.Host.Scheduling;
using FlowEngine.Host.Webhooks;
using FlowEngine.Host.WebSocketHandlers;
using FlowEngine.Infrastructure.Audit;
using FlowEngine.Infrastructure.Identity;
using FlowEngine.Infrastructure.Security;
using FlowEngine.Runtime.Credentials;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Registry;
using FlowEngine.Runtime.Scripting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Quartz;
using System.Text;
using System.Text.Json.Serialization;

namespace FlowEngine.Host;

/// <summary>
/// FlowEngine 服务注册扩展方法。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 FlowEngine 全部服务到 DI 容器。
    /// </summary>
    public static IServiceCollection AddFlowEngine(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        // ── Controllers & JSON ──────────────────────────────────────
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            });
        services.AddMemoryCache();

        // ── Database ────────────────────────────────────────────────
        AddDbContext(services, configuration);

        // ── Infrastructure ──────────────────────────────────────────
        services.AddSingleton<InternalErrorSink>();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddScoped<ParameterResolver>();
        services.AddScoped<CredentialAccessor>();

        services.AddSingleton<AuditLogFileSink>(sp =>
        {
            var logPath = configuration["Audit:LogPath"] ?? "./storage/audit";
            return new AuditLogFileSink(
                logPath,
                sp.GetRequiredService<IEventBus>(),
                sp.GetService<ILogger<AuditLogFileSink>>());
        });
        services.AddSingleton(sp =>
        {
            var logPath = configuration["Audit:LogPath"] ?? "./storage/audit";
            return new AuditLogReader(logPath);
        });

        // ── Authentication & Authorization ──────────────────────────
        AddAuthentication(services, configuration);

        // ── Identity ────────────────────────────────────────────────
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IPasswordValidator, PasswordValidator>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IUserStore, UserStore>();
        services.AddScoped<IUserContext, HttpContextUserContext>();
        services.AddScoped<AuthenticationService>();
        services.AddScoped<AuditEventFactory>();

        // ── Business ────────────────────────────────────────────────
        services.AddSingleton<ICryptoKeyProvider, CryptoKeyProvider>();
        services.AddSingleton<ICredentialEncryptionService, CredentialEncryptionService>();
        services.AddScoped<CredentialService>();
        services.AddScoped<ICredentialAccessor, CredentialAccessor>();
        services.AddScoped<WorkflowValidator>();
        services.AddScoped<WorkflowService>();
        services.AddScoped<TriggerService>();
        services.AddScoped<WebhookHandler>();
        services.AddScoped<ErrorStrategyHandler>();
        services.AddSingleton<WorkflowExecutionQueue>();
        services.AddScoped<WorkflowExecutor>();
        services.AddScoped<IEngine>(sp => sp.GetRequiredService<WorkflowExecutor>());
        services.AddHostedService<WorkflowExecutionWorker>();

        // ── Scheduling & Execution ──────────────────────────────────
        services.AddSingleton<IScheduleManager, QuartzScheduleManager>();
        services.AddQuartz();
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        services.AddScoped<ExecutionService>();
        services.AddScoped<NodeExecutionContextFactory>(provider =>
        {
            var whitelist = configuration.GetSection("Expression:EnvironmentWhitelist").Get<string[]>() ?? [];
            return new NodeExecutionContextFactory(
                provider.GetRequiredService<INodeRegistry>(),
                provider.GetRequiredService<ParameterResolver>(),
                provider.GetRequiredService<ICredentialAccessor>(),
                new HashSet<string>(whitelist, StringComparer.OrdinalIgnoreCase),
                hydratorLogger: provider.GetService<ILogger<ParameterHydrator>>(),
                jsLogger: provider.GetService<ILogger<JsEngine>>());
        });

        // ── WebSocket ───────────────────────────────────────────────
        services.AddSingleton<WebSocketConnectionManager>();
        services.AddSingleton<WebSocketEventPushService>();
        services.AddSingleton<WebSocketReplayService>();
        services.AddScoped<ExecutionWebSocketHandler>();

        // ── CORS ────────────────────────────────────────────────────
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
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

        // ── Plugins & Node Registry ─────────────────────────────────
        var pluginsPath = configuration.GetSection("Plugins")["Path"] ?? "../../plugins";
        if (!Path.IsPathRooted(pluginsPath))
        {
            pluginsPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, pluginsPath));
        }

        services.AddSingleton<PluginLoader>(_ =>
            new PluginLoader(pluginsPath, _.GetRequiredService<ILogger<PluginLoader>>()));

        services.AddSingleton<INodeRegistry>(provider =>
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

        return services;
    }

    private static void AddAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var jwtSecret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("JWT Secret is not configured.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = configuration["Jwt:Issuer"] ?? "FlowEngine",
                    ValidAudience = configuration["Jwt:Audience"] ?? "FlowEngine",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                };
            });
        services.AddAuthorization();
        services.AddHttpContextAccessor();
    }

    private static void AddDbContext(IServiceCollection services, IConfiguration configuration)
    {
        var dbProvider = configuration["Database:Provider"] ?? "sqlite";
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<FlowEngineDbContext>(options =>
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
    }
}
