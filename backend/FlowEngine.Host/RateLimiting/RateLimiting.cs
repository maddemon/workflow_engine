using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using FlowEngine.Application.Audit;
using FlowEngine.Application.RateLimiting;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FlowEngine.Host.RateLimiting;

/// <summary>
/// 限流公共逻辑：路径分类、每客户端分区键计算、白名单判定。
/// 抽取为公共静态方法以便单元测试直接覆盖，并被内置限流器的全局分区器共享，
/// 保证"按路径分策略 + 每客户端独立限流 + 白名单/禁用跳过"的行为一致。
/// </summary>
public static class RateLimitKeyResolver
{
    /// <summary>在 <see cref="HttpContext.Items"/> 中暂存当前命中的策略名，供 <see cref="RateLimiterSetup"/> 的 OnRejected 审计使用。</summary>
    public const string PolicyItemKey = "__RateLimitPolicy";

    /// <summary>
    /// 按客户端身份计算限流分区键：已认证请求按用户 ID，否则按远程 IP；并附加策略名以隔离不同规则。
    /// 与旧实现 <c>RateLimitMiddleware.GetRateLimitKey</c> 的键格式保持一致。
    /// </summary>
    public static string GetClientKey(HttpContext context, string policyName)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
                return $"user:{userId}:{policyName}";
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{ip}:{policyName}";
    }

    /// <summary>
    /// 根据请求路径归类为 Login / Register / Api 策略，并返回对应规则。
    /// 与旧实现 <c>RateLimitMiddleware.ClassifyPath</c> 的归一化与判定保持一致。
    /// </summary>
    public static (string PolicyName, RateLimitRule Rule) ClassifyPath(RateLimitOptions options, string path)
    {
        var normalized = path.TrimEnd('/').ToLowerInvariant();

        if (normalized.Contains("/auth/login") || normalized.Contains("/account/login"))
            return ("Login", options.Login);

        if (normalized.Contains("/auth/register") || normalized.Contains("/account/register"))
            return ("Register", options.Register);

        return ("Api", options.Api);
    }

    /// <summary>判断路径是否命中白名单（大小写不敏感，忽略末尾斜杠）。</summary>
    public static bool IsWhitelisted(RateLimitOptions options, string path)
    {
        var whitelisted = options.WhitelistedPaths;
        if (whitelisted is null || whitelisted.Length == 0)
            return false;

        return whitelisted.Any(wp =>
            string.Equals(wp.TrimEnd('/'), path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// 基于 <see cref="System.Threading.RateLimiting"/> 的限流器配置：
/// 注册一个全局 <see cref="PartitionedRateLimiter{HttpContext}"/>（<see cref="RateLimiterOptions.GlobalLimiter"/>），
/// 其分区器按请求路径归类为 Login / Register / Api 规则，并为每个客户端（用户或 IP）构建独立的
/// <see cref="FixedWindowRateLimiter"/>；白名单或已禁用规则返回无限制分区以跳过限流。
/// 超限时由 <see cref="OnRejected"/> 返回 429 + Retry-After + JSON 并落审计，行为与原手搓中间件一致。
/// </summary>
public static class RateLimiterSetup
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 配置内置限流器：设置全局分区限流器与超限回调。
    /// </summary>
    public static void Configure(RateLimiterOptions options)
    {
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
            BuildPartition,
            StringComparer.Ordinal);

        options.OnRejected = OnRejected;
    }

    /// <summary>
    /// 为单个请求构建分区：按路径分类后返回每客户端独立的固定窗口分区；
    /// 白名单或已禁用规则返回无限制分区（<see cref="RateLimitPartition.GetNoLimiter{TResource}"/>）。
    /// 命中限流规则时把策略名暂存到 <see cref="HttpContext.Items"/> 供审计使用。
    /// </summary>
    private static RateLimitPartition<string> BuildPartition(HttpContext context)
    {
        var rateOptions = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        var path = context.Request.Path.Value ?? string.Empty;

        if (RateLimitKeyResolver.IsWhitelisted(rateOptions, path))
            return RateLimitPartition.GetNoLimiter<string>("__whitelisted");

        var (policyName, rule) = RateLimitKeyResolver.ClassifyPath(rateOptions, path);
        if (!rule.Enabled)
            return RateLimitPartition.GetNoLimiter<string>("__disabled");

        context.Items[RateLimitKeyResolver.PolicyItemKey] = policyName;
        var key = RateLimitKeyResolver.GetClientKey(context, policyName);

        return RateLimitPartition.GetFixedWindowLimiter<string>(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(rule.PermitLimit, 1),
                Window = TimeSpan.FromSeconds(rule.WindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            });
    }

    /// <summary>
    /// 限流被拒时的回调：写入 429 响应、Retry-After 头与 JSON 错误体，并发布审计事件。
    /// </summary>
    private static async ValueTask OnRejected(OnRejectedContext ctx, CancellationToken cancellationToken)
    {
        var http = ctx.HttpContext;
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.ContentType = "application/json; charset=utf-8";

        var retryAfterSeconds = ComputeRetryAfter(ctx.Lease);

        var policyName = http.Items.TryGetValue(RateLimitKeyResolver.PolicyItemKey, out var p) ? (string)p! : "Api";
        var identifier = RateLimitKeyResolver.GetClientKey(http, policyName);

        // 与原中间件一致：发布 RateLimited 审计事件（Security 域，资源 ID 为空）。
        var eventBus = http.RequestServices.GetService<IEventBus>();
        var auditFactory = http.RequestServices.GetService<AuditEventFactory>();
        if (eventBus is not null && auditFactory is not null)
        {
            await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                AuditEventTypes.RateLimited,
                "Security",
                Guid.Empty,
                new Dictionary<string, object>
                {
                    ["identifier"] = identifier,
                    ["rule"] = policyName,
                }),
                cancellationToken).ConfigureAwait(false);
        }

        // Retry-After 头必须在响应体写入之前设置（与统一错误格式保持一致）。
        http.Response.Headers.RetryAfter = retryAfterSeconds.ToString();

        var response = new
        {
            success = false,
            errorCode = "RateLimited",
            message = "请求过于频繁，请稍后再试",
        };

        await http.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions)).ConfigureAwait(false);
    }

    /// <summary>
    /// 从租约元数据中读取建议的等待秒数；缺失时回退到默认窗口（秒）。
    /// </summary>
    private static int ComputeRetryAfter(RateLimitLease lease)
    {
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            return Math.Max((int)Math.Ceiling(retryAfter.TotalSeconds), 1);

        return 60;
    }
}
