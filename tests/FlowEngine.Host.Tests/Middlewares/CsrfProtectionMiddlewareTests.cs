using System.Threading.Tasks;
using FlowEngine.Host.Middlewares;
using FlowEngine.Host.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlowEngine.Host.Tests.Middlewares;

/// <summary>
/// CsrfProtectionMiddleware（S-4）单元测试：仅对携带 fe_auth Cookie 的变更请求要求防伪造头。
/// </summary>
public class CsrfProtectionMiddlewareTests
{
    private static CsrfProtectionMiddleware Create(
        SecurityOptions? options = null,
        RequestDelegate? next = null)
    {
        options ??= new SecurityOptions();
        next ??= _ => Task.CompletedTask;
        return new CsrfProtectionMiddleware(
            next,
            NullLogger<CsrfProtectionMiddleware>.Instance,
            Microsoft.Extensions.Options.Options.Create(options));
    }

    private static HttpContext CreateContext(string method, bool withCookie = false, string? header = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        if (withCookie)
        {
            context.Request.Headers["Cookie"] = "fe_auth=token";
        }

        if (header is not null)
        {
            context.Request.Headers["X-Requested-With"] = header;
        }

        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task Disabled_DoesNotBlock()
    {
        var nextCalled = false;
        var middleware = Create(
            new SecurityOptions { EnableCsrfProtection = false },
            _ => { nextCalled = true; return Task.CompletedTask; });

        var context = CreateContext("POST", withCookie: true, header: null);
        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.NotEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Post_WithCookie_WithoutHeader_Returns403()
    {
        var nextCalled = false;
        var middleware = Create(
            new SecurityOptions { EnableCsrfProtection = true },
            _ => { nextCalled = true; return Task.CompletedTask; });

        var context = CreateContext("POST", withCookie: true, header: null);
        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Post_WithCookie_WithValidHeader_Passes()
    {
        var nextCalled = false;
        var middleware = Create(
            new SecurityOptions { EnableCsrfProtection = true, CsrfHeaderName = "X-Requested-With", CsrfHeaderValue = "FlowEngine" },
            _ => { nextCalled = true; return Task.CompletedTask; });

        var context = CreateContext("POST", withCookie: true, header: "FlowEngine");
        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Post_WithoutCookie_Passes()
    {
        var nextCalled = false;
        var middleware = Create(
            new SecurityOptions { EnableCsrfProtection = true },
            _ => { nextCalled = true; return Task.CompletedTask; });

        var context = CreateContext("POST", withCookie: false, header: null);
        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task BearerRequest_Passes()
    {
        var nextCalled = false;
        var middleware = Create(
            new SecurityOptions { EnableCsrfProtection = true },
            _ => { nextCalled = true; return Task.CompletedTask; });

        var context = CreateContext("POST", withCookie: false, header: null);
        context.Request.Headers["Authorization"] = "Bearer abc.def.ghi";
        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Get_WithCookie_WithoutHeader_Passes()
    {
        var nextCalled = false;
        var middleware = Create(
            new SecurityOptions { EnableCsrfProtection = true },
            _ => { nextCalled = true; return Task.CompletedTask; });

        var context = CreateContext("GET", withCookie: true, header: null);
        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }
}
