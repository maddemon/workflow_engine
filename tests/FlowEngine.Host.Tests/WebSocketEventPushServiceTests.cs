using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Events;
using FlowEngine.Host.WebSocketHandlers;
using Moq;
using Microsoft.Extensions.Logging;
using System.Net.WebSockets;

namespace FlowEngine.Host.Tests;

/// <summary>
/// WebSocketEventPushService 测试。
/// </summary>
public class WebSocketEventPushServiceTests : IDisposable
{
    private readonly Mock<IEventBus> _eventBusMock = new();
    private readonly WebSocketConnectionManager _connectionManager = new();
    private readonly Mock<ILogger<WebSocketReplayService>> _replayLoggerMock = new();
    private readonly WebSocketReplayService _replayService;
    private readonly Mock<ILogger<WebSocketEventPushService>> _loggerMock = new();
    private readonly WebSocketEventPushService _service;

    public WebSocketEventPushServiceTests()
    {
        _replayService = new WebSocketReplayService(_replayLoggerMock.Object);

        _eventBusMock.Setup(e => e.Subscribe(It.IsAny<Func<WorkflowStartedEvent, CancellationToken, Task>>()))
            .Returns(Mock.Of<IDisposable>());
        _eventBusMock.Setup(e => e.Subscribe(It.IsAny<Func<NodeExecutedEvent, CancellationToken, Task>>()))
            .Returns(Mock.Of<IDisposable>());
        _eventBusMock.Setup(e => e.Subscribe(It.IsAny<Func<NodeErrorEvent, CancellationToken, Task>>()))
            .Returns(Mock.Of<IDisposable>());
        _eventBusMock.Setup(e => e.Subscribe(It.IsAny<Func<WorkflowCompletedEvent, CancellationToken, Task>>()))
            .Returns(Mock.Of<IDisposable>());
        _eventBusMock.Setup(e => e.Subscribe(It.IsAny<Func<WorkflowFailedEvent, CancellationToken, Task>>()))
            .Returns(Mock.Of<IDisposable>());
        _eventBusMock.Setup(e => e.Subscribe(It.IsAny<Func<WorkflowCancelledEvent, CancellationToken, Task>>()))
            .Returns(Mock.Of<IDisposable>());

        _service = new WebSocketEventPushService(
            _eventBusMock.Object,
            _connectionManager,
            _replayService,
            _loggerMock.Object);
    }

    [Fact]
    public void Constructor_SubscribesToAllEvents()
    {
        _eventBusMock.Verify(e =>
            e.Subscribe(It.IsAny<Func<WorkflowStartedEvent, CancellationToken, Task>>()), Times.Once);
        _eventBusMock.Verify(e =>
            e.Subscribe(It.IsAny<Func<NodeExecutedEvent, CancellationToken, Task>>()), Times.Once);
        _eventBusMock.Verify(e =>
            e.Subscribe(It.IsAny<Func<NodeErrorEvent, CancellationToken, Task>>()), Times.Once);
        _eventBusMock.Verify(e =>
            e.Subscribe(It.IsAny<Func<WorkflowCompletedEvent, CancellationToken, Task>>()), Times.Once);
        _eventBusMock.Verify(e =>
            e.Subscribe(It.IsAny<Func<WorkflowFailedEvent, CancellationToken, Task>>()), Times.Once);
        _eventBusMock.Verify(e =>
            e.Subscribe(It.IsAny<Func<WorkflowCancelledEvent, CancellationToken, Task>>()), Times.Once);
    }

    [Fact]
    public void Dispose_UnsubscribesFromAllEvents()
    {
        var disposableMock = new Mock<IDisposable>();
        _eventBusMock.Setup(e => e.Subscribe(It.IsAny<Func<WorkflowStartedEvent, CancellationToken, Task>>()))
            .Returns(disposableMock.Object);

        var service = new WebSocketEventPushService(
            _eventBusMock.Object,
            _connectionManager,
            _replayService,
            _loggerMock.Object);

        service.Dispose();

        disposableMock.Verify(d => d.Dispose(), Times.Once);
    }

    public void Dispose()
    {
        _service?.Dispose();
    }
}
