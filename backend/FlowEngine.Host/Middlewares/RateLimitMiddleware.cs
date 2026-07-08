using System.Security.Claims;
using System.Text.Json;
using FlowEngine.Application.Audit;
using FlowEngine.Application.RateLimiting;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FlowEngine.Host.Middlewares;

/// <summary>
/// 基于滑动窗口计数器的速率限制中间件。
/// 匿名请求按 IP 地址限流，已认证请求按用户 ID 限流。
/// </summary>
public class RateLimitMiddleware(
    IMemoryCache cache,
    IOptions<RateLimitOptions> options,
    ILogger<RateLimitMiddleware> logger,
    IEventBus eventBus,
    AuditEventFactory auditFactory) : IMiddleware
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions RateLimitJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 处理请求并执行速率限制检查。
    /// </summary>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (IsWhitelisted(path))
        {
            await next(context);
            return;
        }

        var rule = ClassifyPath(path);
        if (rule is null || !rule.Enabled)
        {
            await next(context);
            return;
        }

        var key = GetRateLimitKey(context, path);
        var now = DateTimeOffset.UtcNow;
        var window = TimeSpan.FromSeconds(rule.WindowSeconds);

        var counter = GetOrCreateCounter(key, now, window);

        var count = counter.RecordRequest(now, window);

        logger.LogDebug("速率限制检查: key={Key}, count={Count}/{Limit}", key, count, rule.PermitLimit);

        if (count > rule.PermitLimit)
        {
            await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                AuditEventTypes.RateLimited,
                "Security",
                Guid.Empty,
                new Dictionary<string, object>
                {
                    ["identifier"] = key,
                    ["rule"] = rule.Key,
                }),
                CancellationToken.None);

            var retryAfter = counter.GetRetryAfterSeconds(now, window);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = retryAfter.ToString();
            context.Response.ContentType = "application/json; charset=utf-8";

            var response = new
            {
                error = "TooManyRequests",
                message = $"Rate limit exceeded. Retry after {retryAfter} seconds.",
                retryAfter,
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response, RateLimitJsonOptions));
            return;
        }

        await next(context);
    }

    private bool IsWhitelisted(string path)
    {
        var whitelisted = options.Value.WhitelistedPaths;
        if (whitelisted is null || whitelisted.Length == 0)
            return false;

        return whitelisted.Any(wp =>
            string.Equals(wp.TrimEnd('/'), path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
    }

    private RateLimitRule? ClassifyPath(string path)
    {
        var normalized = path.TrimEnd('/').ToLowerInvariant();

        if (normalized.Contains("/auth/login") || normalized.Contains("/account/login"))
            return CreateRule(options.Value.Login, "Login");

        if (normalized.Contains("/auth/register") || normalized.Contains("/account/register"))
            return CreateRule(options.Value.Register, "Register");

        return CreateRule(options.Value.Api, "Api");
    }

    private static RateLimitRule CreateRule(RateLimitRule source, string key)
    {
        return new RateLimitRule
        {
            PermitLimit = source.PermitLimit,
            WindowSeconds = source.WindowSeconds,
            Enabled = source.Enabled,
            Key = key,
        };
    }

    private static string GetRateLimitKey(HttpContext context, string path)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
                return $"user:{userId}:{path}";
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{ip}:{path}";
    }

    private SlidingWindowCounter GetOrCreateCounter(string key, DateTimeOffset now, TimeSpan window)
    {
        return cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = window + CleanupInterval;
            return new SlidingWindowCounter();
        })!;
    }
}

/// <summary>
/// 滑动窗口计数器，使用时间戳列表记录请求时间。
/// </summary>
internal sealed class SlidingWindowCounter
{
    private readonly object _lock = new();
    private readonly List<DateTimeOffset> _timestamps = [];

    /// <summary>
    /// 原子地清理过期时间戳并记录当前请求，返回当前窗口内的请求总数。
    /// </summary>
    public long RecordRequest(DateTimeOffset now, TimeSpan window)
    {
        lock (_lock)
        {
            var cutoff = now - window;
            _timestamps.RemoveAll(ts => ts <= cutoff);
            _timestamps.Add(now);
            return _timestamps.Count;
        }
    }

    /// <summary>
    /// 计算需要等待的秒数（基于最早过期的时间戳）。
    /// </summary>
    public int GetRetryAfterSeconds(DateTimeOffset now, TimeSpan window)
    {
        lock (_lock)
        {
            var cutoff = now - window;
            _timestamps.RemoveAll(ts => ts <= cutoff);

            if (_timestamps.Count == 0)
                return (int)window.TotalSeconds;

            var oldest = _timestamps[0];
            var expiresAt = oldest + window;
            var retryAfter = (int)Math.Ceiling((expiresAt - now).TotalSeconds);
            return Math.Max(retryAfter, 1);
        }
    }
}
