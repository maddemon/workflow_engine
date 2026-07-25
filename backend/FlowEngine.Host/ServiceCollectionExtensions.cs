using FlowEngine.Application.Audit;
using FlowEngine.Application.Credentials;
using FlowEngine.Application.Executions;
using FlowEngine.Application.ExecutionCleanup;
using FlowEngine.Application.Files;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Projects;
using FlowEngine.Application.RateLimiting;
using FlowEngine.Application.Security;
using FlowEngine.Application.Triggers;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Credentials;
using FlowEngine.Core.Data;
using FlowEngine.Core.Events;
using FlowEngine.Core.Triggers;
using FlowEngine.Host.Executor;
using FlowEngine.Host.Authentication;
using FlowEngine.Host.Middlewares;
using FlowEngine.Host.Options;
using FlowEngine.Host.Scheduling;
using FlowEngine.Host.Services;
using FlowEngine.Host.Triggers;
using FlowEngine.Host.Webhooks;
using FlowEngine.Host.RateLimiting;
using FlowEngine.Host.WebSocketHandlers;
using FlowEngine.Infrastructure.Audit;
using Microsoft.AspNetCore.RateLimiting;
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
using ModelContextProtocol.AspNetCore;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Quartz;
using System.Text;
using System.Text.Json.Serialization;
using FlowEngine.Resources;
using FlowEngine.Resources.Localization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("FlowEngine.Host.Tests")]

namespace FlowEngine.Host;

/// <summary>
/// FlowEngine 服务注册扩展方法。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 FlowEngine 全部服务到 DI 容器。DEP-7：拆分为按模块组织的 AddFlowEngine* 子方法，
    /// 降低单方法体量，使新增服务不必改动单一巨型方法。
    /// </summary>
    public static IServiceCollection AddFlowEngine(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        AddFlowEnginePresentation(services, configuration);
        AddFlowEngineWebhooks(services, configuration);
        AddFlowEngineSecurity(services, configuration);
        AddFlowEngineInfrastructure(services, configuration);
        AddFlowEngineBusiness(services, configuration);
        AddFlowEngineExecution(services, configuration, environment);
        return services;
    }

    /// <summary>
    /// 表现层：本地化、控制器、MCP、转发头、限流、引擎默认值、CORS 等。
    /// </summary>
    private static void AddFlowEnginePresentation(IServiceCollection services, IConfiguration configuration)
    {
        // ── Localization (JSON-based, embedded resources) ─────────────
        services.AddSingleton<IStringLocalizerFactory>(sp =>
            new JsonStringLocalizerFactory(typeof(SharedResource).Assembly));
        services.AddSingleton<IStringLocalizer>(sp =>
        {
            var factory = sp.GetRequiredService<IStringLocalizerFactory>();
            return factory.Create(typeof(SharedResource));
        });
        services.AddSingleton(typeof(IStringLocalizer<>), typeof(StringLocalizer<>));

        // ── Controllers & JSON ──────────────────────────────────────
        services.AddControllers()
            .AddDataAnnotationsLocalization()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            });
        services.AddMemoryCache();

        // ── MCP Server ──────────────────────────────────────────────
        services.AddMcpServer()
            .WithHttpTransport(options => options.Stateless = false)
            .WithToolsFromAssembly();

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

        // ── Rate Limiting（任务 2.3：System.Threading.RateLimiting 替换手写限流）──
        // 通过 AddRateLimiter 注册全局分区限流器（按路径分类 + 每客户端独立限流），
        // 由 ApplicationBuilderExtensions 中的 app.UseRateLimiter() 接入管道。
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));
        services.AddRateLimiter(RateLimiterSetup.Configure);

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
    }

    /// <summary>
    /// Webhook 相关：重放保护、限流、同步完成通知（SEC-3 / EX-4）。
    /// </summary>
    private static void AddFlowEngineWebhooks(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WebhookOptions>(configuration.GetSection(WebhookOptions.SectionName));
        // SEC-3：Webhook 重放保护 + 按路由/IP 限流配置。
        services.Configure<WebhookSecurityOptions>(configuration.GetSection(WebhookSecurityOptions.SectionName));
        services.AddSingleton<WebhookReplayCache>();
        services.AddSingleton<WebhookRateLimiter>();
        // EX-4：同步 Webhook 事件驱动完成通知（单例桥接执行完成事件与等待中的请求）。
        services.AddSingleton<IWebhookSyncCompletionService, WebhookSyncCompletionService>();
        services.AddSingleton<INotificationHandler<WorkflowCompletedEvent>, WebhookCompletionNotifier>();
    }

    /// <summary>
    /// 安全加固（SEC-2 / S-4 / SEC-1）：安全选项、Shell 门禁、认证与授权。
    /// </summary>
    private static void AddFlowEngineSecurity(IServiceCollection services, IConfiguration configuration)
    {
        // ── Security 加固（SEC-2 / S-4）─────────────────────────────
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));

        // ── Shell 执行门禁（SEC-1）─────────────────────────────────
        services.Configure<ShellOptions>(configuration.GetSection(ShellOptions.SectionName));
        services.AddScoped<Core.Abstractions.IShellExecutionGate, FlowEngine.Application.Security.ShellExecutionGate>();

        // ── Authentication & Authorization ──────────────────────────
        AddAuthentication(services, configuration);
    }

    /// <summary>
    /// 基础设施：数据库、事件总线（MediatR）、审计 Sink 与事件通知处理器。
    /// </summary>
    private static void AddFlowEngineInfrastructure(IServiceCollection services, IConfiguration configuration)
    {
        // ── Database ────────────────────────────────────────────────
        AddDbContext(services, configuration);

        // ── Infrastructure ──────────────────────────────────────────
        // 2.1：事件总线 → MediatR。MediatrEventBus 委托 IMediator 分派通知处理器。
        // 仅扫描 Core 程序集以满足 MediatR 的扫描要求（Core 内无处理器）；
        // 实际事件处理器在 RegisterEventNotificationHandlers 中显式注册以精确控制生命周期。
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(MediatrEventBus).Assembly));
        services.AddSingleton<IEventBus, MediatrEventBus>();
        services.AddScoped<ParameterResolver>();

        // 审计日志 Sink 同时作为单例服务（供 AuditEventNotificationHandler 解析）与托管服务。
        // 任务 2.2：确保 Audit.NET 序列化适配器在宿主启动时注册（幂等；Sink 构造时亦会注册）。
        AuditNetBootstrap.EnsureConfigured();
        services.AddSingleton<AuditLogFileSink>(sp =>
        {
            var logPath = configuration["Audit:LogPath"] ?? "./storage/audit";
            return new AuditLogFileSink(logPath, sp.GetService<ILogger<AuditLogFileSink>>());
        });
        services.AddHostedService(sp => sp.GetRequiredService<AuditLogFileSink>());
        services.AddSingleton<AuditEventNotificationHandler>();

        RegisterEventNotificationHandlers(services);
        services.AddSingleton<IAuditLogReader>(sp =>
        {
            var logPath = configuration["Audit:LogPath"] ?? "./storage/audit";
            return new AuditLogReader(logPath);
        });
    }

    /// <summary>
    /// 业务服务：凭据、工作流、项目、触发器、Webhook 路由等。
    /// </summary>
    private static void AddFlowEngineBusiness(IServiceCollection services, IConfiguration configuration)
    {
        // ── RBAC Authorization ──────────────────────────────────────
        services.AddScoped<FlowEngine.Application.Authorization.AuthorizationService>();
        services.AddScoped<FlowEngine.Application.Authorization.IResourceAuthorizationService,
            FlowEngine.Application.Authorization.ResourceAuthorizationService>();
        services.AddScoped<FlowEngine.Application.Authorization.IAuthorizationGuard,
            FlowEngine.Application.Authorization.AuthorizationGuard>();
        services.AddScoped<FlowEngine.Application.Authorization.AuthorizedOperationHandler>();
        // ── Identity ────────────────────────────────────────────────
        // 模型校验：DataAnnotations 自动校验 + 自定义错误格式
        services.Configure<ApiBehaviorOptions>(o =>
        {
            o.InvalidModelStateResponseFactory = ctx =>
            {
                var errors = ctx.ModelState
                    .Where(e => e.Value?.Errors.Count > 0)
                    .SelectMany(e => e.Value!.Errors.Select(x => x.ErrorMessage))
                    .ToList();
                return new BadRequestObjectResult(new
                {
                    success = false,
                    errorCode = "ValidationFailed",
                    message = string.Join("; ", errors),
                    details = (object?)null
                });
            };
        });
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<PasswordValidator>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IUserStore, UserStore>();
        services.AddScoped<IUserContext, HttpContextUserContext>();
        services.AddScoped<UserRoleService>();
        services.AddScoped<AuthenticationService>();
        services.AddScoped<ApiKeyService>();
        services.AddSingleton<ITokenBlacklist, TokenBlacklistService>();
        services.AddScoped<AuditEventFactory>();

        // ── Business ────────────────────────────────────────────────
        services.AddSingleton<CredentialTypeRegistry>();
        services.AddSingleton<TriggerTypeRegistry>();
        services.AddSingleton<ICryptoKeyProvider, CryptoKeyProvider>();
        services.AddSingleton<ICredentialEncryptionService, CredentialEncryptionService>();
        services.AddScoped<CredentialService>();
        services.AddScoped<WorkflowRepository>();
        // 迁移后补齐 workflow_credential_usages 引用行（按需、幂等）。
        services.AddScoped<WorkflowCredentialUsageBackfill>();
        services.AddHostedService<WorkflowCredentialUsageBackfillHostedService>();
        services.AddScoped<ICredentialAccessor, CredentialAccessor>();
        services.AddScoped<IOAuth2TokenService, OAuth2TokenService>();
        services.AddScoped<WorkflowValidator>();
        services.AddScoped<WorkflowService>();
        services.AddScoped<IWorkflowService>(sp => sp.GetRequiredService<WorkflowService>());
        services.AddScoped<WorkflowStatisticsLoader>();
        services.AddScoped<WorkflowTriggerSync>();
        services.AddScoped<WorkflowExportService>();
        services.AddScoped<WorkflowImportService>();
        services.AddScoped<WorkflowDryRunService>();
        services.AddScoped<WorkflowDraftValidator>();
        services.AddSingleton<CatalogService>();
        services.AddScoped<IWorkflowAssemblyService, WorkflowAssemblyService>();
        services.AddScoped<IWorkflowModificationService, WorkflowModificationService>();
        services.AddScoped<WorkflowValidationService>();
        services.AddScoped<IWorkflowValidationService>(sp => sp.GetRequiredService<WorkflowValidationService>());
        services.AddScoped<IWorkflowExecutionFeedbackService, WorkflowExecutionFeedbackService>();
        services.AddScoped<ProjectService>();
        services.AddScoped<ProjectCascadeDeleter>();
        services.AddScoped<TriggerService>();
        services.AddScoped<WebhookRouteService>();
        services.AddScoped<IWebhookHandler, WebhookHandler>();
        services.AddScoped<ErrorStrategyHandler>();
        services.AddSingleton<WorkflowExecutionQueue>();
        // 按 executionId 索引的执行取消令牌注册表（单例）：worker 登记每执行 CTS，CancelAsync 触发取消。
        services.AddSingleton<ExecutionCancellationRegistry>();
    }

    /// <summary>
    /// 执行相关：文件存储、执行引擎、HTTP 池、调度、执行服务、WebSocket、LLM 工厂、插件注册。
    /// </summary>
    private static void AddFlowEngineExecution(
        IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        // ── File Storage ───────────────────────────────────────────
        services.AddSingleton<IFileStorage>(sp =>
        {
            var basePath = configuration["FileStorage:BasePath"] ?? "./storage/files";
            var logger = sp.GetService<ILogger<LocalFileStorage>>();
            return new LocalFileStorage(basePath, logger);
        });
        services.AddScoped<FileService>();
        services.AddSingleton<FlowEngine.Runtime.Security.SecretMasker>();
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
        services.AddScoped<IExecutionService>(sp => sp.GetRequiredService<ExecutionService>());
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
    }

    /// <summary>
    /// 注册节点执行上下文工厂，集中组合执行节点所需的依赖（A8）。
    /// </summary>
    internal static void AddNodeExecutionContextFactory(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<NodeExecutionContextFactory>(provider =>
        {
            var whitelist = configuration.GetSection("Expression:EnvironmentWhitelist").Get<string[]>() ?? [];
            return new NodeExecutionContextFactory(
                provider.GetRequiredService<INodeRegistry>(),
                provider.GetRequiredService<ScriptCache>(),
                provider.GetRequiredService<ParameterResolver>(),
                provider.GetRequiredService<ICredentialAccessor>(),
                new HashSet<string>(whitelist, StringComparer.OrdinalIgnoreCase),
                hydratorLogger: provider.GetService<ILogger<ParameterHydrator>>(),
                jsLogger: provider.GetService<ILogger<JsEngine>>(),
                jsEngineOptions: provider.GetService<JsEngineOptions>(),
                workflowLoader: provider.GetService<IWorkflowLoader>(),
                httpClientPool: provider.GetService<IHttpClientPool>(),
                tokenService: provider.GetRequiredService<IOAuth2TokenService>(),
                llmClientFactory: provider.GetRequiredService<ILlmClientFactory>(),
                shellExecutionGate: provider.GetService<Core.Abstractions.IShellExecutionGate>(),
                eventBus: provider.GetService<IEventBus>());
        });
    }

    /// <summary>
    /// 注册领域事件通知处理器，将各事件类型映射到对应的单例处理器服务
    /// （<see cref="WebSocketEventPushService"/> 负责实时推送，<see cref="AuditEventNotificationHandler"/> 负责审计落盘）。
    /// 显式注册以精确控制生命周期（处理器服务为单例），避免 MediatR 自动扫描产生 transient 多实例。
    /// </summary>
    private static void RegisterEventNotificationHandlers(IServiceCollection services)
    {
        // WebSocket 实时推送处理器（8 种执行事件）。
        services.AddSingleton<INotificationHandler<WorkflowStartedEvent>>(sp => sp.GetRequiredService<WebSocketEventPushService>());
        services.AddSingleton<INotificationHandler<NodeStartedEvent>>(sp => sp.GetRequiredService<WebSocketEventPushService>());
        services.AddSingleton<INotificationHandler<NodeExecutedEvent>>(sp => sp.GetRequiredService<WebSocketEventPushService>());
        services.AddSingleton<INotificationHandler<NodeErrorEvent>>(sp => sp.GetRequiredService<WebSocketEventPushService>());
        services.AddSingleton<INotificationHandler<WorkflowCompletedEvent>>(sp => sp.GetRequiredService<WebSocketEventPushService>());
        services.AddSingleton<INotificationHandler<WorkflowFailedEvent>>(sp => sp.GetRequiredService<WebSocketEventPushService>());
        services.AddSingleton<INotificationHandler<WorkflowCancelledEvent>>(sp => sp.GetRequiredService<WebSocketEventPushService>());
        services.AddSingleton<INotificationHandler<LlmTokenStreamEvent>>(sp => sp.GetRequiredService<WebSocketEventPushService>());

        // 审计落盘处理器（全部 AuditEvent 子类型）。
        services.AddSingleton<INotificationHandler<AuditLogEvent>>(sp => sp.GetRequiredService<AuditEventNotificationHandler>());
        services.AddSingleton<INotificationHandler<ExecutionCleanupEvent>>(sp => sp.GetRequiredService<AuditEventNotificationHandler>());
        services.AddSingleton<INotificationHandler<WorkflowStartedEvent>>(sp => sp.GetRequiredService<AuditEventNotificationHandler>());
        services.AddSingleton<INotificationHandler<WorkflowCompletedEvent>>(sp => sp.GetRequiredService<AuditEventNotificationHandler>());
        services.AddSingleton<INotificationHandler<WorkflowFailedEvent>>(sp => sp.GetRequiredService<AuditEventNotificationHandler>());
        services.AddSingleton<INotificationHandler<WorkflowCancelledEvent>>(sp => sp.GetRequiredService<AuditEventNotificationHandler>());
        services.AddSingleton<INotificationHandler<NodeStartedEvent>>(sp => sp.GetRequiredService<AuditEventNotificationHandler>());
        services.AddSingleton<INotificationHandler<NodeExecutedEvent>>(sp => sp.GetRequiredService<AuditEventNotificationHandler>());
        services.AddSingleton<INotificationHandler<NodeErrorEvent>>(sp => sp.GetRequiredService<AuditEventNotificationHandler>());
        services.AddSingleton<INotificationHandler<CredentialAccessedEvent>>(sp => sp.GetRequiredService<AuditEventNotificationHandler>());

        // errorTrigger 事件消费者：失败事件 → 启动匹配的工作流（复用 IEngine.StartAsync 统一入口）。
        services.AddSingleton<INotificationHandler<WorkflowFailedEvent>, ErrorTriggerEventConsumer>();
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
        AddAuthorizationPolicies(services, configuration);
        services.AddHttpContextAccessor();
    }

    /// <summary>
    /// SEC-2：注册全局鉴权兜底策略。默认对所有未显式标注 <c>[AllowAnonymous]</c> 的端点
    /// 要求已认证用户，杜绝遗漏 <c>[Authorize]</c> 造成的匿名暴露。安全默认开启，
    /// 可通过配置 <c>Security:RequireAuthenticatedUserByDefault</c> 关闭。
    /// </summary>
    private static void AddAuthorizationPolicies(IServiceCollection services, IConfiguration configuration)
    {
        var secSection = configuration.GetSection(SecurityOptions.SectionName);
        var sec = secSection.Get<SecurityOptions>() ?? new SecurityOptions();

        services.AddAuthorization(options =>
        {
            if (sec.RequireAuthenticatedUserByDefault)
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            }
        });
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
