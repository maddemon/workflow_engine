using FlowEngine.Core.Events;
using FlowEngine.Host.WebSocketHandlers;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Microsoft.Extensions.Logging;
using System.Net.WebSockets;
using MediatR;

namespace FlowEngine.Host.Tests;

/// <summary>
/// WebSocketEventPushService 测试（迁移至 MediatR 通知处理器后）。
/// </summary>
public class WebSocketEventPushServiceTests : IDisposable
{
    private readonly WebSocketConnectionManager _connectionManager = new();
    private readonly Mock<ILogger<WebSocketReplayService>> _replayLoggerMock = new();
    private readonly WebSocketReplayService _replayService;
    private readonly Mock<ILogger<WebSocketEventPushService>> _loggerMock = new();
    private readonly WebSocketEventPushService _service;

    public WebSocketEventPushServiceTests()
    {
        _replayService = new WebSocketReplayService(_replayLoggerMock.Object, Mock.Of<IServiceScopeFactory>());
        _service = new WebSocketEventPushService(_connectionManager, _replayService, _loggerMock.Object);
    }

    [Fact]
    public void Constructor_ImplementsAllNotificationHandlers()
    {
        Assert.IsAssignableFrom<INotificationHandler<WorkflowStartedEvent>>(_service);
        Assert.IsAssignableFrom<INotificationHandler<NodeStartedEvent>>(_service);
        Assert.IsAssignableFrom<INotificationHandler<NodeExecutedEvent>>(_service);
        Assert.IsAssignableFrom<INotificationHandler<NodeErrorEvent>>(_service);
        Assert.IsAssignableFrom<INotificationHandler<WorkflowCompletedEvent>>(_service);
        Assert.IsAssignableFrom<INotificationHandler<WorkflowFailedEvent>>(_service);
        Assert.IsAssignableFrom<INotificationHandler<WorkflowCancelledEvent>>(_service);
        Assert.IsAssignableFrom<INotificationHandler<LlmTokenStreamEvent>>(_service);
    }

    [Fact]
    public async Task Handle_WorkflowStartedEvent_RecordsToReplay()
    {
        var executionId = Guid.NewGuid();

        await ((INotificationHandler<WorkflowStartedEvent>)_service)
            .Handle(new WorkflowStartedEvent(executionId, Guid.NewGuid()), CancellationToken.None);

        var events = _replayService.GetMissingEvents(executionId, 0);
        Assert.Contains(events, e => e.Type == "execution_started");
    }

    [Fact]
    public async Task Handle_NodeExecutedEvent_RecordsToReplay()
    {
        var executionId = Guid.NewGuid();
        var evt = new NodeExecutedEvent(executionId, "node-1", 0, new FlowEngine.Core.Entities.NodeExecutionResult());

        await ((INotificationHandler<NodeExecutedEvent>)_service)
            .Handle(evt, CancellationToken.None);

        var events = _replayService.GetMissingEvents(executionId, 0);
        Assert.Contains(events, e => e.Type == "node_executed");
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        _service.Dispose();
    }

    public void Dispose()
    {
        _service?.Dispose();
        _replayService.Dispose();
    }
}
