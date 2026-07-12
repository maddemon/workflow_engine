using System.Security.Claims;
using FlowEngine.Application.Authorization;
using FlowEngine.Core.Authorization;
using FlowEngine.Host.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;

namespace FlowEngine.Host.Tests.Middlewares;

public class AuthorizationMiddlewareTests
{
    private readonly AuthorizationService _authService = new();

    [Fact]
    public async Task EndpointWithAttribute_ChecksPermission_DeniedReturns403()
    {
        var context = CreateHttpContext("/test");
        var endpoint = CreateEndpointWithAttribute(Scope.Workflow, Operation.Read);
        context.SetEndpoint(endpoint);

        var middleware = new RbacAuthorizationMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, _authService);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task EndpointWithoutAttribute_PassesThrough()
    {
        var context = CreateHttpContext("/test");
        var nextCalled = false;
        var middleware = new RbacAuthorizationMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, _authService);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task DeniedPermission_Returns403WithJson()
    {
        var context = CreateHttpContext("/test");
        var endpoint = CreateEndpointWithAttribute(Scope.Workflow, Operation.Read);
        context.SetEndpoint(endpoint);

        var middleware = new RbacAuthorizationMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, _authService);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);

        context.Response.Body.Seek(0, System.IO.SeekOrigin.Begin);
        var body = await System.Text.Json.JsonSerializer.DeserializeAsync<Dictionary<string, string>>(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(body);
        Assert.Equal("Forbidden", body!["error"]);
    }

    [Fact]
    public async Task AllowedPermission_CallsNext()
    {
        var context = CreateHttpContext("/test");
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "test"));
        var endpoint = CreateEndpointWithAttribute(Scope.Credential, Operation.Read);
        context.SetEndpoint(endpoint);

        var nextCalled = false;
        var middleware = new RbacAuthorizationMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(context, _authService);

        Assert.True(nextCalled);
        Assert.Equal(200, context.Response.StatusCode);
    }

    private static HttpContext CreateHttpContext(string path)
    {
        var context = new DefaultHttpContext
        {
            Request = { Path = path },
            Response = { Body = new MemoryStream() }
        };
        return context;
    }

    private static Endpoint CreateEndpointWithAttribute(Scope scope, Operation operation)
    {
        var metadata = new List<object> { new AuthorizePermissionAttribute(scope, operation) };
        return new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(metadata),
            "TestEndpoint");
    }
}

