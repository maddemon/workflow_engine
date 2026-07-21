using System.Security.Claims;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Events;
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
    private readonly InMemoryEventBus _eventBus = new();
    private readonly AuditEventFactory _auditFactory = new(new FakeUserContext { UserId = Guid.NewGuid() });

    [Fact]
    public async Task EndpointWithAttribute_ChecksPermission_DeniedReturns403()
    {
        var context = CreateHttpContext("/test");
        var endpoint = CreateEndpointWithAttribute(Scope.Workflow, Operation.Read);
        context.SetEndpoint(endpoint);

        var middleware = new RbacAuthorizationMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, _authService, _eventBus, _auditFactory);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task EndpointWithoutAttribute_PassesThrough()
    {
        var context = CreateHttpContext("/test");
        var nextCalled = false;
        var middleware = new RbacAuthorizationMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context, _authService, _eventBus, _auditFactory);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task DeniedPermission_Returns403WithUnifiedErrorEnvelope()
    {
        var context = CreateHttpContext("/test");
        var endpoint = CreateEndpointWithAttribute(Scope.Workflow, Operation.Read);
        context.SetEndpoint(endpoint);

        var middleware = new RbacAuthorizationMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, _authService, _eventBus, _auditFactory);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);

        context.Response.Body.Seek(0, System.IO.SeekOrigin.Begin);
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        var root = doc.RootElement;

        // 统一错误包络：{ success, errorCode, message, details }，与 GlobalExceptionHandlerMiddleware 一致。
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("Forbidden", root.GetProperty("errorCode").GetString());
        Assert.Contains("Workflow:Read", root.GetProperty("message").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, root.GetProperty("details").ValueKind);
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
        await middleware.InvokeAsync(context, _authService, _eventBus, _auditFactory);

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

    private sealed class InMemoryEventBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent => Task.CompletedTask;
        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
            where TEvent : IDomainEvent => new Disposable();
        private sealed class Disposable : IDisposable { public void Dispose() { } }
    }

    private sealed class FakeUserContext : IUserContext
    {
        public bool IsAuthenticated => UserId.HasValue;
        public Guid? UserId { get; init; } = Guid.NewGuid();
        public string? Email => "test@example.com";
        public IReadOnlyList<string> Roles { get; init; } = ["Admin"];
    }
}

