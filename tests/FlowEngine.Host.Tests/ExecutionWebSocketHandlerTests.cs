using System.Buffers;
using System.Threading;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Authorization;
using FlowEngine.Host.WebSocketHandlers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Microsoft.Extensions.Logging;
using System.Net.WebSockets;
using System.Text;

namespace FlowEngine.Host.Tests;

/// <summary>
/// ExecutionWebSocketHandler 集成测试。
/// </summary>
public class ExecutionWebSocketHandlerTests
{
    private readonly WebSocketConnectionManager _connectionManager = new();
    private readonly WebSocketReplayService _replayService = new(
        Mock.Of<ILogger<WebSocketReplayService>>(),
        Mock.Of<IServiceScopeFactory>());
    private readonly Mock<ILogger<ExecutionWebSocketHandler>> _loggerMock = new();
    private readonly Mock<IResourceAuthorizationService> _authzMock = new();

    [Fact]
    public async Task HandleAsync_UnauthenticatedWebSocketRequest_Returns401()
    {
        var userContextMock = new Mock<IUserContext>();
        userContextMock.Setup(u => u.IsAuthenticated).Returns(false);

        var handler = new ExecutionWebSocketHandler(
            _connectionManager,
            _replayService,
            userContextMock.Object,
            _authzMock.Object,
            _loggerMock.Object);

        var context = new DefaultHttpContext();
        context.Features.Set<IHttpWebSocketFeature>(new TestWebSocketFeature());

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
            _authzMock.Object,
            _loggerMock.Object);

        var context = new DefaultHttpContext();

        var nextCalled = false;
        await handler.HandleAsync(context, () => { nextCalled = true; return Task.CompletedTask; });

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task HandleAsync_SubscribeUnauthorizedExecution_DoesNotSubscribe()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var executionId = Guid.NewGuid();

        var userContextMock = new Mock<IUserContext>();
        userContextMock.Setup(u => u.IsAuthenticated).Returns(true);
        userContextMock.Setup(u => u.UserId).Returns(userId);

        _authzMock
            .Setup(a => a.CanAccessExecutionAsync(userId, executionId, Operation.Read, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var subscribeJson = $"{{\"type\":\"subscribe\",\"executionId\":\"{executionId}\"}}";
        var subscribeBytes = Encoding.UTF8.GetBytes(subscribeJson);
        var webSocket = CreateWebSocketForSubscribeThenClose(subscribeBytes);

        var handler = new ExecutionWebSocketHandler(
            _connectionManager,
            _replayService,
            userContextMock.Object,
            _authzMock.Object,
            _loggerMock.Object);

        var context = new DefaultHttpContext();
        context.Features.Set<IHttpWebSocketFeature>(new TestWebSocketFeature(webSocket));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        context.RequestAborted = cts.Token;

        await handler.HandleAsync(context, () => Task.CompletedTask);

        Assert.Empty(_connectionManager.GetConnections(executionId));
        _authzMock.Verify(
            a => a.CanAccessExecutionAsync(userId, executionId, Operation.Read, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_LargeMessageWithMidClose_DoesNotThrow()
    {
        // CON-1：大消息中间收到 Close 时走提前 return 分支，buffer 由 finally 独占归还，
        // 不再与 finally 中的 Return 形成双重归还。此处验证该路径不抛异常、连接最终被清理。
        var userId = Guid.NewGuid();
        var executionId = Guid.NewGuid();

        var userContextMock = new Mock<IUserContext>();
        userContextMock.Setup(u => u.IsAuthenticated).Returns(true);
        userContextMock.Setup(u => u.UserId).Returns(userId);
        _authzMock
            .Setup(a => a.CanAccessExecutionAsync(userId, executionId, Operation.Read, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var subscribeJson = $"{{\"type\":\"subscribe\",\"executionId\":\"{executionId}\"}}";
        var bytes = Encoding.UTF8.GetBytes(subscribeJson);
        var webSocket = new SplitCloseWebSocket(bytes, bytes.Length / 2);

        var handler = new ExecutionWebSocketHandler(
            _connectionManager,
            _replayService,
            userContextMock.Object,
            _authzMock.Object,
            _loggerMock.Object);

        var context = new DefaultHttpContext();
        context.Features.Set<IHttpWebSocketFeature>(new TestWebSocketFeature(webSocket));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        context.RequestAborted = cts.Token;

        await handler.HandleAsync(context, () => Task.CompletedTask);

        Assert.Empty(_connectionManager.GetConnections(executionId));
    }

    [Fact]
    public async Task HandleAsync_NormalSubscribe_RentsAndReturnsBufferExactlyOnce()
    {
        // CON-1 回归守护：正常订阅路径应恰好 Rent 一次、Return 一次（禁止双重归还污染 ArrayPool）。
        var pool = new TrackingArrayPool();
        var userId = Guid.NewGuid();
        var executionId = Guid.NewGuid();

        var userContextMock = new Mock<IUserContext>();
        userContextMock.Setup(u => u.IsAuthenticated).Returns(true);
        userContextMock.Setup(u => u.UserId).Returns(userId);
        _authzMock
            .Setup(a => a.CanAccessExecutionAsync(userId, executionId, Operation.Read, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var subscribeJson = $"{{\"type\":\"subscribe\",\"executionId\":\"{executionId}\"}}";
        var webSocket = CreateWebSocketForSubscribeThenClose(Encoding.UTF8.GetBytes(subscribeJson));

        var handler = new ExecutionWebSocketHandler(
            _connectionManager,
            _replayService,
            userContextMock.Object,
            _authzMock.Object,
            _loggerMock.Object,
            pool);

        var context = new DefaultHttpContext();
        context.Features.Set<IHttpWebSocketFeature>(new TestWebSocketFeature(webSocket));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        context.RequestAborted = cts.Token;

        await handler.HandleAsync(context, () => Task.CompletedTask);

        Assert.Equal(1, pool.RentCount);
        Assert.Equal(1, pool.ReturnCount);
    }

    [Fact]
    public async Task HandleAsync_MidClose_RentsAndReturnsBufferExactlyOnce()
    {
        // CON-1 回归守护：大消息中间收到 Close 的路径同样应恰好 Rent 一次、Return 一次。
        var pool = new TrackingArrayPool();
        var userId = Guid.NewGuid();
        var executionId = Guid.NewGuid();

        var userContextMock = new Mock<IUserContext>();
        userContextMock.Setup(u => u.IsAuthenticated).Returns(true);
        userContextMock.Setup(u => u.UserId).Returns(userId);
        _authzMock
            .Setup(a => a.CanAccessExecutionAsync(userId, executionId, Operation.Read, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var subscribeJson = $"{{\"type\":\"subscribe\",\"executionId\":\"{executionId}\"}}";
        var bytes = Encoding.UTF8.GetBytes(subscribeJson);
        var webSocket = new SplitCloseWebSocket(bytes, bytes.Length / 2);

        var handler = new ExecutionWebSocketHandler(
            _connectionManager,
            _replayService,
            userContextMock.Object,
            _authzMock.Object,
            _loggerMock.Object,
            pool);

        var context = new DefaultHttpContext();
        context.Features.Set<IHttpWebSocketFeature>(new TestWebSocketFeature(webSocket));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        context.RequestAborted = cts.Token;

        await handler.HandleAsync(context, () => Task.CompletedTask);

        Assert.Equal(1, pool.RentCount);
        Assert.Equal(1, pool.ReturnCount);
    }

    private static WebSocket CreateWebSocketForSubscribeThenClose(byte[] messageBytes)
    {
        return new FakeWebSocket(messageBytes);
    }

    private sealed class FakeWebSocket : WebSocket
    {
        private readonly byte[] _messageBytes;
        private int _receiveCount;

        public FakeWebSocket(byte[] messageBytes)
        {
            _messageBytes = messageBytes;
            State = WebSocketState.Open;
        }

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string CloseStatusDescription => string.Empty;

        public override WebSocketState State { get; }

        public override string SubProtocol => string.Empty;

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (_receiveCount == 0)
            {
                _messageBytes.CopyTo(buffer.Array!, buffer.Offset);
                _receiveCount++;
                return Task.FromResult(new WebSocketReceiveResult(_messageBytes.Length, WebSocketMessageType.Text, true));
            }

            return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
        }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override void Abort()
        {
        }

        public override void Dispose()
        {
        }
    }

    private sealed class TestWebSocketFeature : IHttpWebSocketFeature
    {
        private readonly WebSocket? _webSocket;

        public bool IsWebSocketRequest => true;

        public TestWebSocketFeature(WebSocket? webSocket = null)
        {
            _webSocket = webSocket;
        }

        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext acceptContext)
        {
            if (_webSocket is null)
            {
                throw new NotSupportedException();
            }

            return Task.FromResult(_webSocket);
        }
    }

    /// <summary>
    /// 模拟大消息：首帧为非结束的文本片段，第二帧为 Close（命中提前 return 分支，CON-1）。
    /// </summary>
    private sealed class SplitCloseWebSocket : WebSocket
    {
        private readonly byte[] _bytes;
        private readonly int _split;
        private int _step;

        public SplitCloseWebSocket(byte[] bytes, int split)
        {
            _bytes = bytes;
            _split = split;
            State = WebSocketState.Open;
        }

        public override WebSocketState State { get; }

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string CloseStatusDescription => string.Empty;

        public override string SubProtocol => string.Empty;

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (_step == 0)
            {
                var count = Math.Min(_split, buffer.Count);
                Array.Copy(_bytes, 0, buffer.Array!, buffer.Offset, count);
                _step++;
                return Task.FromResult(new WebSocketReceiveResult(count, WebSocketMessageType.Text, false));
            }

            return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
        }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override void Abort()
        {
        }

        public override void Dispose()
        {
        }
    }

    /// <summary>
    /// 计数型 <see cref="ArrayPool{T}"/> 包装，用于断言接收缓冲区恰好被租借/归还一次（CON-1 回归守护）。
    /// </summary>
    private sealed class TrackingArrayPool : ArrayPool<byte>
    {
        private readonly ArrayPool<byte> _inner = ArrayPool<byte>.Shared;

        public int RentCount;
        public int ReturnCount;

        public override byte[] Rent(int minimumLength)
        {
            Interlocked.Increment(ref RentCount);
            return _inner.Rent(minimumLength);
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            Interlocked.Increment(ref ReturnCount);
            _inner.Return(array, clearArray);
        }
    }
}
