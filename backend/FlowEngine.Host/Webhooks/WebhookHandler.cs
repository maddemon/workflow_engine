using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Triggers;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Host.Webhooks;

/// <summary>
/// Webhook 请求处理器。
/// </summary>
public sealed class WebhookHandler
{
    private readonly ITriggerRepository _triggerRepository;
    private readonly IEngine _engine;
    private readonly IEventBus _eventBus;
    private readonly AuditEventFactory _auditFactory;
    private readonly IExecutionStore _executionStore;
    private readonly ILogger<WebhookHandler> _logger;

    /// <summary>
    /// 初始化 Webhook 处理器。
    /// </summary>
    public WebhookHandler(
        ITriggerRepository triggerRepository,
        IEngine engine,
        IEventBus eventBus,
        AuditEventFactory auditFactory,
        IExecutionStore executionStore,
        ILogger<WebhookHandler> logger)
    {
        _triggerRepository = triggerRepository ?? throw new ArgumentNullException(nameof(triggerRepository));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _eventBus = eventBus;
        _auditFactory = auditFactory;
        _executionStore = executionStore;
        _logger = logger;
    }

    /// <summary>
    /// 处理 Webhook 请求。
    /// </summary>
    public async Task HandleAsync(HttpContext context, string routePath)
    {
        var route = await _triggerRepository
            .GetWebhookRouteByPathAsync(routePath, context.RequestAborted)
            .ConfigureAwait(false);

        if (route is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = "Webhook route not found" }, context.RequestAborted)
                .ConfigureAwait(false);
            return;
        }

        var trigger = await _triggerRepository
            .GetByIdAsync(route.TriggerId, context.RequestAborted)
            .ConfigureAwait(false);

        if (trigger is null || !trigger.IsActive)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = "Webhook route not found" }, context.RequestAborted)
                .ConfigureAwait(false);
            return;
        }

        if (!await ValidateRequestAsync(context, route).ConfigureAwait(false))
        {
            return;
        }

        object? payload = null;
        if (context.Request.ContentLength > 0)
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
            payload = JsonSerializer.Deserialize<Dictionary<string, object>>(body);
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
            var executionId = await _engine.StartAsync(
                route.WorkflowDefinitionId,
                triggerPayload: new { triggerType = "Webhook", routePath, payload },
                context.RequestAborted).ConfigureAwait(false);

            if (route.IsSync)
            {
                var maxWait = TimeSpan.FromSeconds(route.MaxWaitSeconds);
                var startWait = DateTime.UtcNow;

                while (DateTime.UtcNow - startWait < maxWait)
                {
                    var record = await _executionStore.GetByIdAsync(executionId.Value, context.RequestAborted)
                        .ConfigureAwait(false);

                    if (record is not null && record.Status is ExecutionStatus.Completed or ExecutionStatus.Failed)
                    {
                        context.Response.StatusCode = StatusCodes.Status200OK;
                        await context.Response.WriteAsJsonAsync(
                            new { executionId = executionId.Value, status = record.Status.ToString() },
                            context.RequestAborted).ConfigureAwait(false);
                        return;
                    }

                    await Task.Delay(100, context.RequestAborted).ConfigureAwait(false);
                }

                context.Response.StatusCode = StatusCodes.Status200OK;
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

    private async Task<bool> ValidateRequestAsync(HttpContext context, WebhookRoute route)
    {
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

            var body = await ReadBodyAsync(context.Request).ConfigureAwait(false);
            var expectedHash = ComputeHmacSha256(route.Secret, body);
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

            context.Request.Body.Position = 0;
        }

        if (!string.IsNullOrEmpty(route.AllowedIpsJson))
        {
            var allowedIps = JsonSerializer.Deserialize<List<string>>(route.AllowedIpsJson);
            if (allowedIps?.Count > 0)
            {
                var remoteIp = context.Connection.RemoteIpAddress?.ToString();
                if (string.IsNullOrEmpty(remoteIp) || !allowedIps.Contains(remoteIp))
                {
                    _logger.LogWarning("Webhook IP 白名单拒绝: Path={Path}, IP={IP}", route.Path, remoteIp);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(
                        new { error = "IP not allowed" },
                        context.RequestAborted).ConfigureAwait(false);
                    return false;
                }
            }
        }

        if (!string.IsNullOrEmpty(route.AllowedOriginsJson))
        {
            var allowedOrigins = JsonSerializer.Deserialize<List<string>>(route.AllowedOriginsJson);
            if (allowedOrigins?.Count > 0)
            {
                if (context.Request.Headers.TryGetValue("Origin", out var originValues))
                {
                    var origin = originValues.FirstOrDefault();
                    if (string.IsNullOrEmpty(origin) || !allowedOrigins.Contains(origin))
                    {
                        _logger.LogWarning("Webhook 来源域拒绝: Path={Path}, Origin={Origin}", route.Path, origin);
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(
                            new { error = "Origin not allowed" },
                            context.RequestAborted).ConfigureAwait(false);
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static async Task<string> ReadBodyAsync(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private static string ComputeHmacSha256(string secret, string body)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var hash = HMACSHA256.HashData(keyBytes, bodyBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
