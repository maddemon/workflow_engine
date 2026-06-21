using FlowEngine.Core.Events;
using FlowEngine.Host.WebSocketHandlers;
using Moq;
using System.Net.WebSockets;

namespace FlowEngine.Host.Tests;

/// <summary>
/// WebSocketConnectionManager 测试。
/// </summary>
public class WebSocketConnectionManagerTests
{
    private readonly WebSocketConnectionManager _manager = new();

    [Fact]
    public void Subscribe_AddsConnectionToExecution()
    {
        var executionId = Guid.NewGuid();
        var connection = CreateMockConnection();

        _manager.Subscribe(executionId, connection);

        var connections = _manager.GetConnections(executionId);
        Assert.Single(connections);
        Assert.Equal(connection.ConnectionId, connections.First().ConnectionId);
    }

    [Fact]
    public void Subscribe_MultipleConnectionsSameExecution()
    {
        var executionId = Guid.NewGuid();
        var conn1 = CreateMockConnection();
        var conn2 = CreateMockConnection();

        _manager.Subscribe(executionId, conn1);
        _manager.Subscribe(executionId, conn2);

        var connections = _manager.GetConnections(executionId);
        Assert.Equal(2, connections.Count);
    }

    [Fact]
    public void Unsubscribe_RemovesConnectionFromExecution()
    {
        var executionId = Guid.NewGuid();
        var connection = CreateMockConnection();

        _manager.Subscribe(executionId, connection);
        _manager.Unsubscribe(executionId, connection);

        var connections = _manager.GetConnections(executionId);
        Assert.Empty(connections);
    }

    [Fact]
    public void RemoveConnection_CleansUpAllSubscriptions()
    {
        var executionId1 = Guid.NewGuid();
        var executionId2 = Guid.NewGuid();
        var connection = CreateMockConnection();

        _manager.Subscribe(executionId1, connection);
        _manager.Subscribe(executionId2, connection);

        _manager.RemoveConnection(connection);

        Assert.Empty(_manager.GetConnections(executionId1));
        Assert.Empty(_manager.GetConnections(executionId2));
    }

    [Fact]
    public void GetSubscriptions_ReturnsSubscribedExecutionIds()
    {
        var executionId1 = Guid.NewGuid();
        var executionId2 = Guid.NewGuid();
        var connection = CreateMockConnection();

        _manager.Subscribe(executionId1, connection);
        _manager.Subscribe(executionId2, connection);

        var subscriptions = _manager.GetSubscriptions(connection);
        Assert.Equal(2, subscriptions.Count);
        Assert.Contains(executionId1, subscriptions);
        Assert.Contains(executionId2, subscriptions);
    }

    [Fact]
    public void GetConnections_ReturnsEmptyForUnsubscribedExecution()
    {
        var executionId = Guid.NewGuid();
        var connections = _manager.GetConnections(executionId);
        Assert.Empty(connections);
    }

    private static WebSocketConnection CreateMockConnection()
    {
        var mockWebSocket = new Mock<WebSocket>();
        mockWebSocket.SetupGet(w => w.State).Returns(WebSocketState.Open);
        return new WebSocketConnection(mockWebSocket.Object);
    }
}
