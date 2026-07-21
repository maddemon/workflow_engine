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
        IOptions<WebhookOptions> options)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _eventBus = eventBus;
        _auditFactory = auditFactory;
        _idempotencyService = idempotencyService;
        _logger = logger;
        _options = options.Value;
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
                var startWait = DateTime.UtcNow;

                while (DateTime.UtcNow - startWait < maxWait)
                {
                    var record = await _dbContext.ExecutionRecords
                        .FirstOrDefaultAsync(e => e.Id == executionId.Value, context.RequestAborted)
                        .ConfigureAwait(false);

                    if (record is not null && record.Status is ExecutionStatus.Completed or ExecutionStatus.Failed)
                    {
                        context.Response.StatusCode = StatusCodes.Status200OK;
                        await context.Response.WriteAsJsonAsync(
                            new { executionId = executionId.Value, status = record.Status.ToString() },
                            context.RequestAborted).ConfigureAwait(false);
                        return;
                    }

                    await Task.Delay(_options.PollingIntervalMs, context.RequestAborted).ConfigureAwait(false);
                }

                context.Response.StatusCode = StatusCodes.Status202Accepted;
                await context.Response.WriteAsJsonAsync(
                    new { executionId = executionId.Value, status = "Timeout" },
                    context.RequestAborted).ConfigureAwait(false);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                await context.Response.WriteAsJsonAsync(
                    new { executionId = executionId.Value },
                    context.RequestAborted).ConfigureAwait(false);
            }
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

        if (!string.IsNullOrEmpty(route.Secret))
        {
            if (!context.Request.Headers.TryGetValue("X-Hub-Signature-256", out var signatureValues))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(
                    new { error = "Missing signature" },
                    context.RequestAborted).ConfigureAwait(false);
                return false;
            }

            var signature = signatureValues.FirstOrDefault();
            if (string.IsNullOrEmpty(signature))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(
                    new { error = "Empty signature" },
                    context.RequestAborted).ConfigureAwait(false);
                return false;
            }

            var expectedHash = ComputeHmacSha256(route.Secret, rawBody ?? string.Empty);
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
