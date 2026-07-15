using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using FlowEngine.Application.Authorization;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Events;
using FlowEngine.Host.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Host.Tests.Controllers;

/// <summary>
/// SSE 兜底端点真实测试：直接驱动 <see cref="SseController.Stream"/>，验证
/// Content-Type、SSE 事件帧格式（event:/data:）以及事件总线订阅装配，
/// 而非仅断言 DTO 字段（原测试为假绿）。
/// </summary>
public sealed class ExecutionSseFallbackTests
{
    [Fact]
    public async Task Stream_Returns_Sse_ContentType_And_ConnectedFrame()
    {
        var executionId = Guid.NewGuid();
        var bus = new CapturingEventBus();
        var controller = CreateController(bus);

        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        var services = new ServiceCollection();
        services.AddSingleton(new JsonSerializerOptions());
        httpContext.RequestServices = services.BuildServiceProvider();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));

#pragma warning disable xUnit1051
        var result = await controller.Stream(executionId, cts.Token);
#pragma warning restore xUnit1051

        // 连接建立后立即 yield "connected" 帧，随后阻塞在 ReadAllAsync；
        // 超时取消即可安全结束并读取已刷新的首帧。
        try
        {
            await result.ExecuteAsync(httpContext);
        }
        catch (OperationCanceledException)
        {
            // 预期：客户端断开/超时触发的取消。
        }

        Assert.Equal("text/event-stream", httpContext.Response.ContentType);

        httpContext.Response.Body.Position = 0;
#pragma warning disable xUnit1051
        var body = await new StreamReader(httpContext.Response.Body).ReadToEndAsync();
#pragma warning restore xUnit1051

        // SSE 帧格式：event: <type>\ndata: <json>\n\n
        Assert.Contains("event: connected", body);
        Assert.Contains("data:", body);
        Assert.Contains(executionId.ToString(), body);
    }

    [Fact]
    public void Stream_Subscribes_To_All_ExecutionDomainEvents()
    {
        var executionId = Guid.NewGuid();
        var bus = new CapturingEventBus();
        var controller = CreateController(bus);

        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        _ = controller.Stream(executionId, CancellationToken.None);

        var subscribedTypes = bus.Subscriptions.Select(s => s.EventType).ToHashSet();
        Assert.Contains(typeof(WorkflowStartedEvent), subscribedTypes);
        Assert.Contains(typeof(NodeStartedEvent), subscribedTypes);
        Assert.Contains(typeof(NodeExecutedEvent), subscribedTypes);
        Assert.Contains(typeof(NodeErrorEvent), subscribedTypes);
        Assert.Contains(typeof(WorkflowCompletedEvent), subscribedTypes);
        Assert.Contains(typeof(WorkflowFailedEvent), subscribedTypes);
        Assert.Contains(typeof(WorkflowCancelledEvent), subscribedTypes);
        Assert.Contains(typeof(LlmTokenStreamEvent), subscribedTypes);
    }

    private static SseController CreateController(CapturingEventBus bus)
    {
        return new SseController(
            bus,
            new AllowAllAuthorizationGuard(),
            NullLogger<SseController>.Instance);
    }

    private sealed class AllowAllAuthorizationGuard : IAuthorizationGuard
    {
        public Task RequireAccessAsync(ResourceKind kind, Guid resourceId, Operation operation, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequireScopeAsync(Scope scope, Operation operation, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequireAdminAsync(Operation operation, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CapturingEventBus : IEventBus
    {
        public List<(Type EventType, Delegate Handler)> Subscriptions { get; } = [];

        public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
            => Task.CompletedTask;

        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
            where TEvent : IDomainEvent
        {
            Subscriptions.Add((typeof(TEvent), handler));
            return new NoopDisposable();
        }

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
