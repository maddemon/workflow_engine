using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Triggers;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Host.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlowEngine.Host.Webhooks;

/// <summary>
/// Webhook 请求处理器。
/// </summary>
public sealed class WebhookHandler : IWebhookHandler
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly IEngine _engine;
    private readonly IEventBus _eventBus;
    private readonly AuditEventFactory _auditFactory;
    private readonly IExecutionIdempotencyService _idempotencyService;
    private readonly ILogger<WebhookHandler> _logger;
    private readonly WebhookOptions _options;
    private readonly WebhookReplayCache _replayCache;
    private readonly WebhookRateLimiter _rateLimiter;
    private readonly WebhookSecurityOptions _securityOptions;
    private readonly IWebhookSyncCompletionService _syncCompletion;

    /// <summary>
    /// 初始化 Webhook 处理器。
    /// </summary>
    public WebhookHandler(
        FlowEngineDbContext dbContext,
        IEngine engine,
        IEventBus eventBus,
        AuditEventFactory auditFactory,
        IExecutionIdempotencyService idempotencyService,
        ILogger<WebhookHandler> logger,
        IOptions<WebhookOptions> options,
        WebhookReplayCache replayCache,
        WebhookRateLimiter rateLimiter,
        IOptions<WebhookSecurityOptions> securityOptions,
        IWebhookSyncCompletionService syncCompletion)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _eventBus = eventBus;
        _auditFactory = auditFactory;
        _idempotencyService = idempotencyService;
        _logger = logger;
        _options = options.Value;
        _replayCache = replayCache ?? throw new ArgumentNullException(nameof(replayCache));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _securityOptions = securityOptions?.Value ?? throw new ArgumentNullException(nameof(securityOptions));
        _syncCompletion = syncCompletion ?? throw new ArgumentNullException(nameof(syncCompletion));
    }

    /// <summary>
    /// 处理 Webhook 请求。
    /// </summary>
    public async Task HandleAsync(HttpContext context, string routePath)
    {
        var route = await _dbContext.WebhookRoutes
            .FirstOrDefaultAsync(r => r.Path == routePath, context.RequestAborted)
            .ConfigureAwait(false);

        if (route is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = "Webhook route not found" }, context.RequestAborted)
                .ConfigureAwait(false);
            return;
        }

        var trigger = await _dbContext.Triggers
            .FirstOrDefaultAsync(t => t.Id == route.TriggerId, context.RequestAborted)
            .ConfigureAwait(false);

        if (trigger is null || !trigger.IsActive)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = "Webhook route not found" }, context.RequestAborted)
                .ConfigureAwait(false);
            return;
        }

        // 请求体仅读取一次，供签名校验与 payload 解析复用；
        // 不依赖流的 Seek/Position 回退，避免在非 seekable 请求流（如 Kestrel）上崩溃（B8 修复）。
        // 契约：空 body（ContentLength == 0）时 rawBody 为 null，签名分支以 string.Empty 参与 HMAC，
        // 与无 body 的客户端约定一致，后续改动勿破坏此契约。
        string? rawBody = null;
        if (context.Request.ContentLength > 0)
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            rawBody = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
        }

        if (!await ValidateRequestAsync(context, route, rawBody).ConfigureAwait(false))
        {
            return;
        }

        object? payload = null;
        if (!string.IsNullOrEmpty(rawBody))
        {
            payload = JsonSerializer.Deserialize<Dictionary<string, object>>(rawBody);
        }

        var metadata = new Dictionary<string, string>
        {
            ["ip"] = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            ["userAgent"] = context.Request.Headers.UserAgent.ToString(),
            ["path"] = routePath,
        };

        await _eventBus.PublishAsync(_auditFactory.Create<AuditLogEvent>(
            AuditEventTypes.WebhookTriggered,
            "WebhookRoute",
            route.Id,
            new Dictionary<string, object>
            {
                ["workflowDefinitionId"] = route.WorkflowDefinitionId,
                ["triggerId"] = route.TriggerId,
                ["path"] = routePath,
            },
            metadata),
            context.RequestAborted).ConfigureAwait(false);

        try
        {
            // 幂等检查：如果配置了幂等键模板，解析模板并检查是否已执行
            string? idempotencyKey = null;
            if (!string.IsNullOrWhiteSpace(trigger.Settings.IdempotencyKeyTemplate))
            {
                idempotencyKey = ResolveIdempotencyKeyTemplate(trigger.Settings.IdempotencyKeyTemplate, context, payload);
                if (!string.IsNullOrEmpty(idempotencyKey))
                {
                    // 仅查询，不预注册：先检查是否已有执行，避免使用临时 ExecutionId 造成竞态
                    var existingExecutionId = await _idempotencyService.TryGetExistingAsync(
                        idempotencyKey, context.RequestAborted).ConfigureAwait(false);
                    if (existingExecutionId.HasValue)
                    {
                        _logger.LogInformation(
                            "Webhook 幂等命中: Key={Key}, ExecutionId={ExecutionId}", idempotencyKey, existingExecutionId.Value);
                        context.Response.StatusCode = StatusCodes.Status200OK;
                        await context.Response.WriteAsJsonAsync(
                            new { executionId = existingExecutionId.Value, status = "Idempotent" },
                            context.RequestAborted).ConfigureAwait(false);
                        return;
                    }
                }
            }

            var executionId = await _engine.StartAsync(
                route.WorkflowDefinitionId,
                triggerPayload: new { triggerType = "Webhook", routePath, payload },
                context.RequestAborted).ConfigureAwait(false);

            // StartAsync 成功后，用真实 ExecutionId 注册幂等键
            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                var ttl = trigger.Settings.IdempotencyTtlSeconds.HasValue
                    ? TimeSpan.FromSeconds(trigger.Settings.IdempotencyTtlSeconds.Value)
                    : TimeSpan.FromSeconds(3600);
                await _idempotencyService.TryGetOrRegisterAsync(
                    idempotencyKey, executionId.Value, ttl, context.RequestAborted).ConfigureAwait(false);
            }

            if (route.IsSync)
            {
                var maxWait = TimeSpan.FromSeconds(route.MaxWaitSeconds);
                try
                {
                    // EX-4：事件驱动等待执行完成，取代原先周期性查询 ExecutionRecords 的 DB 轮询循环。
                    var status = await _syncCompletion
                        .WaitAsync(executionId.Value, maxWait, context.RequestAborted)
                        .ConfigureAwait(false);

                    context.Response.StatusCode = StatusCodes.Status200OK;
                    await context.Response.WriteAsJsonAsync(
                        new { executionId = executionId.Value, status = status.ToString() },
                        context.RequestAborted).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
                {
                    // 等待完成事件超时：返回 202，交由调用方稍后查询最终状态。
                    context.Response.StatusCode = StatusCodes.Status202Accepted;
                    await context.Response.WriteAsJsonAsync(
                        new { executionId = executionId.Value, status = "Timeout" },
                        context.RequestAborted).ConfigureAwait(false);
                    return;
                }
            }

            context.Response.StatusCode = StatusCodes.Status202Accepted;
            await context.Response.WriteAsJsonAsync(
                new { executionId = executionId.Value },
                context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook 触发工作流失败: RoutePath={RoutePath}", routePath);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(
                new { error = "Failed to start workflow" },
                context.RequestAborted).ConfigureAwait(false);
        }
    }

    private async Task<bool> ValidateRequestAsync(HttpContext context, WebhookRoute route, string? rawBody)
    {
        // H3：未配置密钥时，必须显式配置 IP 白名单作为替代防护，否则拒绝匿名触发。
        // 避免空 Secret 使任意匿名 POST 直接触发工作流执行。
        if (string.IsNullOrEmpty(route.Secret) && (route.AllowedIps is not { Count: > 0 }))
        {
            _logger.LogWarning("Webhook 未配置密钥且无 IP 白名单，拒绝匿名触发: Path={Path}", route.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(
                new { error = "Webhook requires a secret or IP allowlist" },
                context.RequestAborted).ConfigureAwait(false);
            return false;
        }

        // SEC-3：重放保护与限流——要求时间戳与 nonce，并校验新鲜度/唯一性/配额。
        if (!await ValidateReplayAndRateAsync(context, route).ConfigureAwait(false))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(route.Secret))
        {
            if (!context.Request.Headers.TryGetValue("X-Hub-Signature-256", out var signatureValues)
                || string.IsNullOrEmpty(signatureValues.FirstOrDefault()))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(
                    new { error = "Missing signature" },
                    context.RequestAborted).ConfigureAwait(false);
                return false;
            }

            // SEC-3：签名绑定 timestamp+nonce，使捕获的请求无法在窗口外被重放。
            var signature = signatureValues.FirstOrDefault()!;
            var timestamp = context.Request.Headers["X-Webhook-Timestamp"].ToString();
            var nonce = context.Request.Headers["X-Webhook-Nonce"].ToString();
            var expectedHash = ComputeHmacSha256(route.Secret, $"{timestamp}.{nonce}.{rawBody ?? string.Empty}");
            var expected = $"sha256={expectedHash}";

            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(signature)))
            {
                _logger.LogWarning("Webhook 签名验证失败: Path={Path}", route.Path);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(
                    new { error = "Invalid signature" },
                    context.RequestAborted).ConfigureAwait(false);
                return false;
            }
        }

        if (route.AllowedIps?.Count > 0)
        {
            var remoteIp = context.Connection.RemoteIpAddress?.ToString();
            if (string.IsNullOrEmpty(remoteIp) || !route.AllowedIps.Contains(remoteIp))
            {
                _logger.LogWarning("Webhook IP 白名单拒绝: Path={Path}, IP={IP}", route.Path, remoteIp);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    new { error = "IP not allowed" },
                    context.RequestAborted).ConfigureAwait(false);
                return false;
            }
        }

        if (route.AllowedOrigins?.Count > 0)
        {
            if (!context.Request.Headers.TryGetValue("Origin", out var originValues))
            {
                _logger.LogWarning("Webhook 来源域拒绝: Path={Path}, Origin=<缺失>", route.Path);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    new { error = "Origin header required" },
                    context.RequestAborted).ConfigureAwait(false);
                return false;
            }

            var origin = originValues.FirstOrDefault();
            if (string.IsNullOrEmpty(origin) || !route.AllowedOrigins.Contains(origin))
            {
                _logger.LogWarning("Webhook 来源域拒绝: Path={Path}, Origin={Origin}", route.Path, origin);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    new { error = "Origin not allowed" },
                    context.RequestAborted).ConfigureAwait(false);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// SEC-3：校验 Webhook 请求的防重放与限流。
    /// 当启用重放保护或路由配置了签名密钥时，要求 <c>X-Webhook-Timestamp</c> 与 <c>X-Webhook-Nonce</c> 头
    /// （二者被 HMAC 签名绑定，且重放校验消费它们）；否则这两个头非必需。
    /// 限流仅依赖路由路径 + 客户端 IP，与这两个头无关，始终在启用时执行。
    /// </summary>
    private async Task<bool> ValidateReplayAndRateAsync(HttpContext context, WebhookRoute route)
    {
        // timestamp+nonce 头仅在以下情况为必需：
        //  - 启用了重放保护（重放校验消费这两个头）；
        //  - 或路由配置了签名密钥（HMAC 将 timestamp+nonce 绑定进签名，缺失则无法验签）。
        // 限流仅依赖 路由路径 + 客户端 IP，与这两个头无关，因此不要求它们。
        var timestampNonceRequired = _securityOptions.EnableReplayProtection
                                     || !string.IsNullOrEmpty(route.Secret);

        if (timestampNonceRequired)
        {
            var timestampHeader = context.Request.Headers["X-Webhook-Timestamp"].ToString();
            var nonce = context.Request.Headers["X-Webhook-Nonce"].ToString();

            if (string.IsNullOrWhiteSpace(timestampHeader) || string.IsNullOrWhiteSpace(nonce))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(
                    new { error = "Missing X-Webhook-Timestamp or X-Webhook-Nonce" },
                    context.RequestAborted).ConfigureAwait(false);
                return false;
            }

            if (!long.TryParse(timestampHeader, out var timestampUnix))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(
                    new { error = "Invalid X-Webhook-Timestamp" },
                    context.RequestAborted).ConfigureAwait(false);
                return false;
            }

            // 仅当重放保护启用时才做重放校验；route.Secret 仅用于 HMAC 签名（在签名步骤校验）。
            if (_securityOptions.EnableReplayProtection)
            {
                var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (!_replayCache.TryAccept(route.Path, nonce, timestampUnix, nowUnix, out var replayError))
                {
                    _logger.LogWarning("Webhook 重放校验失败: Path={Path}, Reason={Reason}", route.Path, replayError);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(
                        new { error = "Replay protection rejected request" },
                        context.RequestAborted).ConfigureAwait(false);
                    return false;
                }
            }
        }

        // 限流与 timestamp/nonce 无关，仅在启用时执行（不受上方头要求影响）。
        if (_securityOptions.EnableRateLimit)
        {
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (!_rateLimiter.TryAcquire(route.Path, remoteIp, nowUnix))
            {
                _logger.LogWarning("Webhook 限流触发: Path={Path}, IP={IP}", route.Path, remoteIp);
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsJsonAsync(
                    new { error = "Too many requests" },
                    context.RequestAborted).ConfigureAwait(false);
                return false;
            }
        }

        return true;
    }

    private static string ComputeHmacSha256(string secret, string body)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var hash = HMACSHA256.HashData(keyBytes, bodyBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 解析幂等键模板，支持 {headers.X-Header-Name} 和 {body.fieldName} 变量。
    /// </summary>
    private static string ResolveIdempotencyKeyTemplate(
        string template,
        HttpContext context,
        object? payload)
    {
        var result = template;

        // 替换 {headers.Xxx} → 请求头
        result = Regex.Replace(result, @"\{headers\.([^}]+)\}", match =>
        {
            var headerName = match.Groups[1].Value;
            if (context.Request.Headers.TryGetValue(headerName, out var values))
            {
                return values.FirstOrDefault() ?? string.Empty;
            }
            return string.Empty;
        }, RegexOptions.IgnoreCase);

        // 替换 {body.xxx} → 请求体字段
        if (payload is Dictionary<string, object> bodyDict)
        {
            result = Regex.Replace(result, @"\{body\.([^}]+)\}", match =>
            {
                var fieldName = match.Groups[1].Value;
                return bodyDict.TryGetValue(fieldName, out var value) ? value?.ToString() ?? string.Empty : string.Empty;
            }, RegexOptions.IgnoreCase);
        }

        return result;
    }


}
