using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Events;
using FlowEngine.Host.WebSocketHandlers;
using Moq;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Host.Tests;

/// <summary>
/// WebSocketReplayService 测试。
/// </summary>
public class WebSocketReplayServiceTests : IDisposable
{
    private readonly Mock<IEventBus> _eventBusMock = new();
    private readonly Mock<IUserContext> _userContextMock = new();
    private readonly Mock<ILogger<WebSocketReplayService>> _loggerMock = new();
    private readonly WebSocketReplayService _service;

    public WebSocketReplayServiceTests()
    {
        _userContextMock.Setup(u => u.IsAuthenticated).Returns(true);
        _userContextMock.Setup(u => u.UserId).Returns(Guid.NewGuid());

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

        _service = new WebSocketReplayService(
            _eventBusMock.Object,
            _userContextMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public void GetMissingEvents_ReturnsEmptyForNoHistory()
    {
        var executionId = Guid.NewGuid();
        var missing = _service.GetMissingEvents(executionId, 0);
        Assert.Empty(missing);
    }

    [Fact]
    public void GetMissingEvents_ReturnsEmptyForNonExistentExecution()
    {
        var executionId = Guid.NewGuid();
        var missing = _service.GetMissingEvents(executionId, 100);
        Assert.Empty(missing);
    }

    [Fact]
    public void Dispose_ClearsHistory()
    {
        _service.Dispose();
        var missing = _service.GetMissingEvents(Guid.NewGuid(), 0);
        Assert.Empty(missing);
    }

    public void Dispose()
    {
        _service?.Dispose();
    }
}
