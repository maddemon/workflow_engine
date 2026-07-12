using FlowEngine.Application.Audit;
using FlowEngine.Application.Credentials;
using FlowEngine.Application.Executions;
using FlowEngine.Application.ExecutionCleanup;
using FlowEngine.Application.Files;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Projects;
using FlowEngine.Application.RateLimiting;
using FlowEngine.Application.Triggers;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Credentials;
using FlowEngine.Core.Data;
using FlowEngine.Core.Events;
using FlowEngine.Host.Executor;
using FlowEngine.Host.Authentication;
using FlowEngine.Host.Middlewares;
using FlowEngine.Host.Scheduling;
using FlowEngine.Host.Services;
using FlowEngine.Host.Webhooks;
using FlowEngine.Host.WebSocketHandlers;
using FlowEngine.Infrastructure.Audit;
using FlowEngine.Infrastructure.Identity;
using FlowEngine.Infrastructure.Security;
using FlowEngine.Infrastructure.Storage;
using FlowEngine.Runtime.Credentials;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Http;
using FlowEngine.Runtime.Registry;
using FlowEngine.Core.DependencyInjection;
using FlowEngine.Core.Scripting;
using FlowEngine.Infrastructure.Ai;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
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

        // ── Forwarded Headers（反向代理信任）─────────────────
        // 仅在配置了 KnownProxies/KnownNetworks 时，X-Forwarded-For 等才会生效，
        // 防止客户端伪造 X-Forwarded-For 绕过基于 IP 的限流（L2）。
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost;
            var proxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>();
            if (proxies is not null)
            {
                foreach (var p in proxies)
                {
                    if (IPAddress.TryParse(p, out var ip))
                    {
                        options.KnownProxies.Add(ip);
                    }
                }
            }

            var networks = configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>();
            if (networks is not null)
            {
                foreach (var n in networks)
                {
                    var parts = n.Split('/');
                    if (parts.Length == 2
                        && IPAddress.TryParse(parts[0], out var addr)
                        && int.TryParse(parts[1], out var prefix))
                    {
                        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse($"{parts[0]}/{parts[1]}"));
                    }
                }
            }
        });

        // ── Rate Limiting ───────────────────────────────────────────
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));
        services.AddTransient<RateLimitMiddleware>();

        // ── File Storage ───────────────────────────────────────────
        services.Configure<FlowEngine.Application.Files.FileStorageOptions>(
            configuration.GetSection(FlowEngine.Application.Files.FileStorageOptions.SectionName));

        // ── Execution Cleanup ──────────────────────────────────────
        services.Configure<ExecutionCleanupOptions>(configuration.GetSection(ExecutionCleanupOptions.SectionName));
        services.AddScoped<ExecutionCleanupService>();
        services.AddHostedService<ExecutionCleanupHostedService>();

        // ── Engine Defaults ────────────────────────────────────────
        services.Configure<EngineDefaultsOptions>(configuration.GetSection(EngineDefaultsOptions.SectionName));
        services.AddFlowEngineCoreScripting();

        // ── Database ────────────────────────────────────────────────
        AddDbContext(services, configuration);

        // ── Infrastructure ──────────────────────────────────────────
        services.AddSingleton<InternalErrorSink>();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddScoped<ParameterResolver>();

        services.AddHostedService(sp =>
        {
            var logPath = configuration["Audit:LogPath"] ?? "./storage/audit";
            return new AuditLogFileSink(
                logPath,
                sp.GetRequiredService<IEventBus>(),
                sp.GetService<ILogger<AuditLogFileSink>>());
        });
        services.AddSingleton<IAuditLogReader>(sp =>
        {
            var logPath = configuration["Audit:LogPath"] ?? "./storage/audit";
            return new AuditLogReader(logPath);
        });

        // ── Authentication & Authorization ──────────────────────────
        AddAuthentication(services, configuration);

        // ── RBAC Authorization ──────────────────────────────────────
        services.AddScoped<FlowEngine.Application.Authorization.IAuthorizationService,
            FlowEngine.Application.Authorization.AuthorizationService>();
        services.AddScoped<FlowEngine.Application.Authorization.IResourceAuthorizationService,
            FlowEngine.Application.Authorization.ResourceAuthorizationService>();
        services.AddScoped<FlowEngine.Application.Authorization.IAuthorizationGuard,
            FlowEngine.Application.Authorization.AuthorizationGuard>();
        services.AddScoped<FlowEngine.Application.Authorization.AuthorizedOperationHandler>();
        // ── Identity ────────────────────────────────────────────────
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IPasswordValidator, PasswordValidator>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IUserStore, UserStore>();
        services.AddScoped<IUserContext, HttpContextUserContext>();
        services.AddScoped<IUserRoleService, UserRoleService>();
        services.AddScoped<AuthenticationService>();
        services.AddScoped<ApiKeyService>();
        services.AddSingleton<ITokenBlacklist, TokenBlacklistService>();
        services.AddScoped<AuditEventFactory>();

        // ── Business ────────────────────────────────────────────────
        services.AddSingleton<ICredentialTypeRegistry, CredentialTypeRegistry>();
        services.AddSingleton<ICryptoKeyProvider, CryptoKeyProvider>();
        services.AddSingleton<ICredentialEncryptionService, CredentialEncryptionService>();
        services.AddScoped<CredentialService>();
        services.AddScoped<WorkflowRepository>();
        services.AddScoped<ICredentialAccessor, CredentialAccessor>();
        services.AddScoped<IOAuth2TokenService, OAuth2TokenService>();
        services.AddScoped<WorkflowValidator>();
        services.AddScoped<WorkflowService>();
        services.AddScoped<WorkflowStatisticsLoader>();
        services.AddScoped<WorkflowTriggerSync>();
        services.AddScoped<WorkflowExportService>();
        services.AddScoped<WorkflowImportService>();
        services.AddScoped<WorkflowDryRunService>();
        services.AddScoped<WorkflowDraftValidator>();
        services.AddSingleton<CatalogService>();
        services.AddScoped<WorkflowAssemblyService>();
        services.AddScoped<WorkflowModificationService>();
        services.AddScoped<WorkflowValidationService>();
        services.AddScoped<WorkflowExecutionFeedbackService>();
        services.AddScoped<ProjectService>();
        services.AddScoped<ProjectCascadeDeleter>();
        services.AddScoped<TriggerService>();
        services.AddScoped<WebhookRouteService>();
        services.AddScoped<IWebhookHandler, WebhookHandler>();
        services.AddScoped<ErrorStrategyHandler>();
        services.AddSingleton<WorkflowExecutionQueue>();

        // ── File Storage ───────────────────────────────────────────
        services.AddSingleton<IFileStorage>(sp =>
        {
            var basePath = configuration["FileStorage:BasePath"] ?? "./storage/files";
            var logger = sp.GetService<ILogger<LocalFileStorage>>();
            return new LocalFileStorage(basePath, logger);
        });
        services.AddScoped<FileService>();
        services.AddSingleton<FlowEngine.Runtime.Security.ISecretMasker, FlowEngine.Runtime.Security.SecretMasker>();
        services.AddScoped<WorkflowExecutor>();
        services.AddScoped<IEngine>(sp => sp.GetRequiredService<WorkflowExecutor>());
        services.AddHostedService<WorkflowExecutionWorker>();

        // ── HTTP Client Pool ─────────────────────────────────────────
        services.AddHttpClient();
        services.AddSingleton<IHttpClientPool, HttpClientPool>();

        // ── Scheduling & Execution ──────────────────────────────────
        services.AddSingleton<IScheduleManager, QuartzScheduleManager>();
        services.AddQuartz();
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        services.AddScoped<ExecutionService>();
        services.AddScoped<IExecutionIdempotencyService, ExecutionIdempotencyService>();
        services.AddScoped<IWorkflowLoader, WorkflowLoader>();
        services.AddNodeExecutionContextFactory(configuration);

        // ── WebSocket ───────────────────────────────────────────────
        services.AddSingleton<WebSocketConnectionManager>();
        services.AddSingleton<WebSocketEventPushService>();
        services.AddSingleton<WebSocketReplayService>();
        services.AddScoped<ExecutionWebSocketHandler>();

        // ── LLM 客户端工厂（运行时节点）─────────────────────────────
        // 插件（如 LlmNode、AgentNode）按运行时参数创建 LLM 客户端的工厂，
        // 抽象定义于 Core，实现位于 Infrastructure。通过执行上下文注入，
        // 使插件仅依赖 Core 抽象而不直接引用 Infrastructure 具体类型。
        services.AddSingleton<ILlmClientFactory, OpenAiLlmClientFactory>();

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
                    // 未配置允许的跨域源时，默认拒绝所有跨域请求（仅同源可访问），避免 CORS 全开放（H4）。
                    policy.WithOrigins()
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

    /// <summary>
    /// 注册节点执行上下文工厂，集中组合执行节点所需的依赖（A8）。
    /// </summary>
    private static void AddNodeExecutionContextFactory(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<NodeExecutionContextFactory>(provider =>
        {
            var whitelist = configuration.GetSection("Expression:EnvironmentWhitelist").Get<string[]>() ?? [];
            return new NodeExecutionContextFactory(
                provider.GetRequiredService<INodeRegistry>(),
                provider.GetRequiredService<IScriptCache>(),
                provider.GetRequiredService<ParameterResolver>(),
                provider.GetRequiredService<ICredentialAccessor>(),
                new HashSet<string>(whitelist, StringComparer.OrdinalIgnoreCase),
                hydratorLogger: provider.GetService<ILogger<ParameterHydrator>>(),
                jsLogger: provider.GetService<ILogger<JsEngine>>(),
                jsEngineOptions: provider.GetService<JsEngineOptions>(),
                workflowLoader: provider.GetService<IWorkflowLoader>(),
                httpClientPool: provider.GetService<IHttpClientPool>(),
                tokenService: provider.GetRequiredService<IOAuth2TokenService>(),
                llmClientFactory: provider.GetRequiredService<ILlmClientFactory>());
        });
    }

    private static void AddAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var jwtSecret = configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(jwtSecret))
        {
            throw new InvalidOperationException(
                "JWT Secret 未配置。请在环境变量或配置中设置 Jwt:Secret，长度至少 32 字节。");
        }

        var jwtSecretBytes = Encoding.UTF8.GetBytes(jwtSecret);
        if (jwtSecretBytes.Length < 32)
        {
            throw new InvalidOperationException(
                $"JWT Secret 长度不足：当前 {jwtSecretBytes.Length} 字节（UTF-8），至少需要 32 字节。请使用强随机密钥。");
        }

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = "BearerOrApiKey";
                options.DefaultAuthenticateScheme = "BearerOrApiKey";
                options.DefaultChallengeScheme = "BearerOrApiKey";
            })
            .AddPolicyScheme("BearerOrApiKey", "Bearer or API Key", options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
                    if (!string.IsNullOrEmpty(authHeader) &&
                        authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        var token = authHeader["Bearer ".Length..];
                        // JWT 由三段组成，以 '.' 分隔；其余视为 API Key。
                        return token.Count(c => c == '.') == 2
                            ? JwtBearerDefaults.AuthenticationScheme
                            : "ApiKey";
                    }

                    return JwtBearerDefaults.AuthenticationScheme;
                };
            })
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

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken))
                        {
                            context.Token = accessToken;
                        }
                        else
                        {
                            // L1/H5：同源浏览器请求（含 WS/SSE）自动携带 HttpOnly Cookie，
                            // 从中读取 JWT，避免令牌经 URL 暴露。
                            var cookie = context.Request.Cookies["fe_auth"];
                            if (!string.IsNullOrEmpty(cookie))
                            {
                                context.Token = cookie;
                            }
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var blacklist = context.HttpContext.RequestServices.GetRequiredService<ITokenBlacklist>();
                        var jti = context.SecurityToken.Id;
                        if (!string.IsNullOrEmpty(jti) && await blacklist.IsBlacklistedAsync(jti, CancellationToken.None).ConfigureAwait(false))
                        {
                            context.Fail("Token has been revoked.");
                        }
                    },
                };
            })
            .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", _ => { });
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
