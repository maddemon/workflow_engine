using System.Text.Json;
using FlowEngine.Core.Exceptions;
using FlowEngine.Host.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Host.Tests.Middlewares;

public class GlobalExceptionHandlerMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NoException_CallsNextAndReturnsOk()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionHandlerMiddleware(
            _ => Task.CompletedTask,
            NullLogger<GlobalExceptionHandlerMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_GenericException_Returns500()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionHandlerMiddleware(
            _ => throw new InvalidOperationException("boom"),
            NullLogger<GlobalExceptionHandlerMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", context.Response.ContentType);
        var payload = await ReadPayloadAsync(context.Response);
        Assert.Equal("InternalServerError", payload.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task InvokeAsync_PermissionDeniedException_Returns403()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionHandlerMiddleware(
            _ => throw new PermissionDeniedException("denied"),
            NullLogger<GlobalExceptionHandlerMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        var payload = await ReadPayloadAsync(context.Response);
        Assert.Equal("Forbidden", payload.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task InvokeAsync_UnauthorizedException_Returns401()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionHandlerMiddleware(
            _ => throw new UnauthorizedException("unauthorized"),
            NullLogger<GlobalExceptionHandlerMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        var payload = await ReadPayloadAsync(context.Response);
        Assert.Equal("Unauthorized", payload.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task InvokeAsync_NotFoundException_Returns404()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionHandlerMiddleware(
            _ => throw new NotFoundException("missing"),
            NullLogger<GlobalExceptionHandlerMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        var payload = await ReadPayloadAsync(context.Response);
        Assert.Equal("NotFound", payload.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task InvokeAsync_BusinessException_Returns400()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionHandlerMiddleware(
            _ => throw new BusinessException("invalid"),
            NullLogger<GlobalExceptionHandlerMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        var payload = await ReadPayloadAsync(context.Response);
        Assert.Equal("BadRequest", payload.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task InvokeAsync_ArgumentException_Returns400()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionHandlerMiddleware(
            _ => throw new ArgumentException("bad arg"),
            NullLogger<GlobalExceptionHandlerMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        var payload = await ReadPayloadAsync(context.Response);
        Assert.Equal("BadRequest", payload.GetProperty("errorCode").GetString());
    }

    private static async Task<JsonElement> ReadPayloadAsync(HttpResponse response)
    {
        response.Body.Position = 0;
        var reader = new StreamReader(response.Body);
        var json = await reader.ReadToEndAsync();
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public async Task InvokeAsync_CustomDomainException_MapsTo400ByBaseType()
    {
        // EX-1：任何未单独列出的 DomainException 子类按基类映射为 400（验证中间件按基类映射，
        // 新增领域异常无需逐一登记即可获得正确状态码）。
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionHandlerMiddleware(
            _ => throw new CustomDomainException("domain rule violated"),
            NullLogger<GlobalExceptionHandlerMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        var payload = await ReadPayloadAsync(context.Response);
        Assert.Equal("BadRequest", payload.GetProperty("errorCode").GetString());
    }

    private sealed class CustomDomainException(string message) : DomainException(message);}

