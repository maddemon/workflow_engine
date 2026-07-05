using System.Security.Claims;
using System.Text.Json;
using FlowEngine.Application.Audit;
using FlowEngine.Application.RateLimiting;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Events;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FlowEngine.Host.Middlewares;

/// <summary>
/// 基于滑动窗口计数器的速率限制中间件。
/// 匿名请求按 IP 地址限流，已认证请求按用户 ID 限流。
/// </summary>
public class RateLimitMiddleware(
    RequestDelegate next,
    IMemoryCache cache,
    IOptions<RateLimitOptions> options,
    ILogger<RateLimitMiddleware> logger)
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 处理请求并执行速率限制检查。
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
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

        lock (counter)
        {
            counter.CleanupExpiredEntries(now, window);
            counter.Increment(now);
        }

        logger.LogDebug("速率限制检查: key={Key}, count={Count}/{Limit}", key, counter.Count, rule.PermitLimit);

        if (counter.Count > rule.PermitLimit)
        {
            var identifier = GetRateLimitKey(context, path);
            var eventBus = context.RequestServices.GetRequiredService<IEventBus>();
            var auditFactory = context.RequestServices.GetRequiredService<AuditEventFactory>();
            await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                AuditEventTypes.RateLimited,
                "Security",
                Guid.Empty,
                new Dictionary<string, object>
                {
                    ["identifier"] = identifier,
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

            await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }));
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
    private readonly List<DateTimeOffset> _timestamps = [];
    private long _totalInWindow;

    /// <summary>
    /// 当前窗口内的请求总数。
    /// </summary>
    public long Count => _totalInWindow;

    /// <summary>
    /// 清理过期的时间戳记录。
    /// </summary>
    public void CleanupExpiredEntries(DateTimeOffset now, TimeSpan window)
    {
        var cutoff = now - window;
        var removed = _timestamps.RemoveAll(ts => ts <= cutoff);
        _totalInWindow -= removed;
    }

    /// <summary>
    /// 记录一次新请求。
    /// </summary>
    public void Increment(DateTimeOffset now)
    {
        _timestamps.Add(now);
        _totalInWindow++;
    }

    /// <summary>
    /// 计算需要等待的秒数（基于最早过期的时间戳）。
    /// </summary>
    public int GetRetryAfterSeconds(DateTimeOffset now, TimeSpan window)
    {
        if (_timestamps.Count == 0)
            return (int)window.TotalSeconds;

        var oldest = _timestamps[0];
        var expiresAt = oldest + window;
        var retryAfter = (int)Math.Ceiling((expiresAt - now).TotalSeconds);
        return Math.Max(retryAfter, 1);
    }
}
