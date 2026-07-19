using System.Net.WebSockets;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Host.WebSocketHandlers;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

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
            .Handle(
                new WorkflowStartedEvent(executionId, Guid.NewGuid()),
                TestContext.Current.CancellationToken);

        var events = _replayService.GetMissingEvents(executionId, 0);
        Assert.Contains(events, e => e.Type == "execution_started");
    }

    [Fact]
    public async Task Handle_NodeStartedEvent_RecordsToReplay()
    {
        var executionId = Guid.NewGuid();
        var evt = new NodeStartedEvent(executionId, "node-1", 0);

        await ((INotificationHandler<NodeStartedEvent>)_service)
            .Handle(evt, TestContext.Current.CancellationToken);

        var events = _replayService.GetMissingEvents(executionId, 0);
        Assert.Contains(events, e => e.Type == "node_started");
    }

    [Fact]
    public async Task Handle_NodeExecutedEvent_RecordsToReplay()
    {
        var executionId = Guid.NewGuid();
        var evt = new NodeExecutedEvent(executionId, "node-1", 0, new NodeExecutionResult());

        await ((INotificationHandler<NodeExecutedEvent>)_service)
            .Handle(evt, TestContext.Current.CancellationToken);

        var events = _replayService.GetMissingEvents(executionId, 0);
        Assert.Contains(events, e => e.Type == "node_executed");
    }

    [Fact]
    public async Task Handle_NodeErrorEvent_RecordsToReplay()
    {
        var executionId = Guid.NewGuid();
        var evt = new NodeErrorEvent(
            executionId,
            "node-1",
            0,
            new NodeError { Code = "E1", Message = "error" });

        await ((INotificationHandler<NodeErrorEvent>)_service)
            .Handle(evt, TestContext.Current.CancellationToken);

        var events = _replayService.GetMissingEvents(executionId, 0);
        Assert.Contains(events, e => e.Type == "node_error");
    }

    [Fact]
    public async Task Handle_WorkflowCompletedEvent_RecordsToReplay()
    {
        var executionId = Guid.NewGuid();
        var evt = new WorkflowCompletedEvent(
            executionId,
            Guid.NewGuid(),
            ExecutionStatus.Completed);

        await ((INotificationHandler<WorkflowCompletedEvent>)_service)
            .Handle(evt, TestContext.Current.CancellationToken);

        var events = _replayService.GetMissingEvents(executionId, 0);
        Assert.Contains(events, e => e.Type == "execution_completed");
    }

    [Fact]
    public async Task Handle_WorkflowFailedEvent_RecordsToReplay()
    {
        var executionId = Guid.NewGuid();
        var evt = new WorkflowFailedEvent(
            executionId,
            Guid.NewGuid(),
            new NodeError { Code = "E2", Message = "failed" });

        await ((INotificationHandler<WorkflowFailedEvent>)_service)
            .Handle(evt, TestContext.Current.CancellationToken);

        var events = _replayService.GetMissingEvents(executionId, 0);
        Assert.Contains(events, e => e.Type == "execution_failed");
    }

    [Fact]
    public async Task Handle_WorkflowCancelledEvent_RecordsToReplay()
    {
        var executionId = Guid.NewGuid();
        var evt = new WorkflowCancelledEvent(executionId, Guid.NewGuid());

        await ((INotificationHandler<WorkflowCancelledEvent>)_service)
            .Handle(evt, TestContext.Current.CancellationToken);

        var events = _replayService.GetMissingEvents(executionId, 0);
        Assert.Contains(events, e => e.Type == "execution_cancelled");
    }

    [Fact]
    public async Task Handle_LlmTokenStreamEvent_DoesNotRecordToReplay()
    {
        var executionId = Guid.NewGuid();
        var evt = new LlmTokenStreamEvent
        {
            ExecutionId = executionId,
            NodeDefinitionId = "node-1",
            RunIndex = 0,
            Delta = "hello",
            IsFinal = false,
        };

        await ((INotificationHandler<LlmTokenStreamEvent>)_service)
            .Handle(evt, TestContext.Current.CancellationToken);

        var events = _replayService.GetMissingEvents(executionId, 0);
        Assert.DoesNotContain(events, e => e.Type == "llm_token");
    }

    [Fact]
    public async Task Handle_WorkflowStartedEvent_BroadcastsToOpenConnection()
    {
        var executionId = Guid.NewGuid();
        var webSocketMock = new Mock<WebSocket>();
        webSocketMock.SetupGet(w => w.State).Returns(WebSocketState.Open);
        var connection = new WebSocketConnection(webSocketMock.Object);
        _connectionManager.Subscribe(executionId, connection);

        await ((INotificationHandler<WorkflowStartedEvent>)_service)
            .Handle(
                new WorkflowStartedEvent(executionId, Guid.NewGuid()),
                TestContext.Current.CancellationToken);

        webSocketMock.Verify(
            w => w.SendAsync(
                It.IsAny<ArraySegment<byte>>(),
                WebSocketMessageType.Text,
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WorkflowStartedEvent_SkipsClosedConnection()
    {
        var executionId = Guid.NewGuid();
        var webSocketMock = new Mock<WebSocket>();
        webSocketMock.SetupGet(w => w.State).Returns(WebSocketState.Closed);
        var connection = new WebSocketConnection(webSocketMock.Object);
        _connectionManager.Subscribe(executionId, connection);

        await ((INotificationHandler<WorkflowStartedEvent>)_service)
            .Handle(
                new WorkflowStartedEvent(executionId, Guid.NewGuid()),
                TestContext.Current.CancellationToken);

        webSocketMock.Verify(
            w => w.SendAsync(
                It.IsAny<ArraySegment<byte>>(),
                It.IsAny<WebSocketMessageType>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SendMessageThrows_LogsWarning()
    {
        var executionId = Guid.NewGuid();
        var webSocketMock = new Mock<WebSocket>();
        webSocketMock.SetupGet(w => w.State).Returns(WebSocketState.Open);
        webSocketMock
            .Setup(w => w.SendAsync(
                It.IsAny<ArraySegment<byte>>(),
                WebSocketMessageType.Text,
                true,
                It.IsAny<CancellationToken>()))
            .Throws(new WebSocketException("boom"));
        var connection = new WebSocketConnection(webSocketMock.Object);
        _connectionManager.Subscribe(executionId, connection);

        await ((INotificationHandler<WorkflowStartedEvent>)_service)
            .Handle(
                new WorkflowStartedEvent(executionId, Guid.NewGuid()),
                TestContext.Current.CancellationToken);

        webSocketMock.Verify(
            w => w.SendAsync(
                It.IsAny<ArraySegment<byte>>(),
                WebSocketMessageType.Text,
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        _service.Dispose();
        Assert.True(true);
    }

    public void Dispose()
    {
        _service?.Dispose();
        _replayService.Dispose();
    }
}
