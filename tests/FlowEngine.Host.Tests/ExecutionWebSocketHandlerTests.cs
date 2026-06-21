using FlowEngine.Application.Identity;
using FlowEngine.Host.WebSocketHandlers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Moq;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Host.Tests;

/// <summary>
/// ExecutionWebSocketHandler 集成测试。
/// </summary>
public class ExecutionWebSocketHandlerTests
{
    private readonly WebSocketConnectionManager _connectionManager = new();
    private readonly WebSocketReplayService _replayService = new(Mock.Of<ILogger<WebSocketReplayService>>());
    private readonly Mock<ILogger<ExecutionWebSocketHandler>> _loggerMock = new();

    [Fact]
    public async Task HandleAsync_UnauthenticatedWebSocketRequest_Returns401()
    {
        var userContextMock = new Mock<IUserContext>();
        userContextMock.Setup(u => u.IsAuthenticated).Returns(false);

        var handler = new ExecutionWebSocketHandler(
            _connectionManager,
            _replayService,
            userContextMock.Object,
            _loggerMock.Object);

        var context = new DefaultHttpContext();
        context.Features.Set<IHttpWebSocketFeature>(new TestWebSocketFeature(isWebSocketRequest: true));

        var nextCalled = false;
        await handler.HandleAsync(context, () => { nextCalled = true; return Task.CompletedTask; });

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task HandleAsync_NonWebSocketRequest_CallsNext()
    {
        var userContextMock = new Mock<IUserContext>();
        userContextMock.Setup(u => u.IsAuthenticated).Returns(true);

        var handler = new ExecutionWebSocketHandler(
            _connectionManager,
            _replayService,
            userContextMock.Object,
            _loggerMock.Object);

        var context = new DefaultHttpContext();

        var nextCalled = false;
        await handler.HandleAsync(context, () => { nextCalled = true; return Task.CompletedTask; });

        Assert.True(nextCalled);
    }

    private sealed class TestWebSocketFeature : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest { get; }

        public TestWebSocketFeature(bool isWebSocketRequest)
        {
            IsWebSocketRequest = isWebSocketRequest;
        }

        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext acceptContext)
        {
            throw new NotSupportedException();
        }
    }
}
