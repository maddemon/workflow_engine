using System.Threading.Tasks;
using FlowEngine.Host.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlowEngine.Host.Middlewares;

/// <summary>
/// Cookie 认证 CSRF 防护中间件（S-4）。
/// <para>
/// 仅对“携带 <c>fe_auth</c> Cookie 的变更请求”要求自定义防伪造请求头
/// （<see cref="SecurityOptions.CsrfHeaderName"/> = <c>X-Requested-With: FlowEngine</c>）。
/// 缺失该头即拒绝（403 Forbidden）。
/// </para>
/// <para>
/// 设计取舍：跨站伪造请求（浏览器自动携带 Cookie）无法设置自定义请求头，
/// 因此要求该头即可阻断 CSRF，同时不影响同源 SPA 与 Bearer/API Key 请求。
/// 该头不依赖 Cookie 本身，仅作为“非浏览器自动发起”的信号。
/// </para>
/// 说明：Bearer / API Key 请求经 <c>Authorization</c> 头认证，不受 Cookie-CSRF 影响，直接放行；
/// 匿名请求（无 <c>fe_auth</c> Cookie）亦放行，其防护由各自的认证机制负责。
/// </summary>
public sealed class CsrfProtectionMiddleware
{
    private static readonly HashSet<string> s_mutatingMethods =
    [
        HttpMethods.Post, HttpMethods.Put, HttpMethods.Delete, HttpMethods.Patch,
    ];

    private const string CookieAuthName = "fe_auth";

    private readonly RequestDelegate _next;
    private readonly ILogger<CsrfProtectionMiddleware> _logger;
    private readonly SecurityOptions _options;

    public CsrfProtectionMiddleware(
        RequestDelegate next,
        ILogger<CsrfProtectionMiddleware> logger,
        IOptions<SecurityOptions> options)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.EnableCsrfProtection
            || !s_mutatingMethods.Contains(context.Request.Method)
            || !HasCookieAuth(context)
            || HasValidCsrfHeader(context))
        {
            await _next(context);
            return;
        }

        _logger.LogWarning(
            "CSRF 防护拦截：请求携带 Cookie 但缺少防伪造头 {Header}。Path={Path}, Method={Method}",
            _options.CsrfHeaderName, context.Request.Path, context.Request.Method);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            success = false,
            errorCode = "CsrfValidationFailed",
            message = "Missing required anti-forgery header."
        }, context.RequestAborted).ConfigureAwait(false);
    }

    private static bool HasCookieAuth(HttpContext context)
        => context.Request.Cookies.ContainsKey(CookieAuthName);

    private bool HasValidCsrfHeader(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(_options.CsrfHeaderName, out var values))
        {
            return false;
        }

        var headerValue = values.FirstOrDefault();
        return !string.IsNullOrEmpty(headerValue)
            && string.Equals(headerValue, _options.CsrfHeaderValue, StringComparison.Ordinal);
    }
}
