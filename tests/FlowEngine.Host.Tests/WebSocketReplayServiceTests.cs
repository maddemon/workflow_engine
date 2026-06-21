using FlowEngine.Host.WebSocketHandlers;
using Moq;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Host.Tests;

/// <summary>
/// WebSocketReplayService 测试。
/// </summary>
public class WebSocketReplayServiceTests : IDisposable
{
    private readonly Mock<ILogger<WebSocketReplayService>> _loggerMock = new();
    private readonly WebSocketReplayService _service;

    public WebSocketReplayServiceTests()
    {
        _service = new WebSocketReplayService(_loggerMock.Object);
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
    public void RecordEvent_StoresEvent()
    {
        var executionId = Guid.NewGuid();
        var message = new WebSocketPushMessage
        {
            Type = "execution_started",
            ExecutionId = executionId,
            Sequence = 1,
        };

        _service.RecordEvent(executionId, message);

        var missing = _service.GetMissingEvents(executionId, 0);
        Assert.Single(missing);
        Assert.Equal(1, missing[0].Sequence);
    }

    [Fact]
    public void RecordEvent_EvictsOldEventsWhenOverLimit()
    {
        var executionId = Guid.NewGuid();
        for (int i = 1; i <= 1001; i++)
        {
            _service.RecordEvent(executionId, new WebSocketPushMessage
            {
                Type = "node_executed",
                ExecutionId = executionId,
                Sequence = i,
            });
        }

        var events = _service.GetMissingEvents(executionId, 0);
        Assert.Equal(1000, events.Count);
        Assert.Equal(2, events[0].Sequence);
        Assert.Equal(1001, events[^1].Sequence);
    }

    [Fact]
    public void GetMissingEvents_ReturnsEventsAfterLastSequence()
    {
        var executionId = Guid.NewGuid();
        for (int i = 1; i <= 5; i++)
        {
            _service.RecordEvent(executionId, new WebSocketPushMessage
            {
                Type = "node_executed",
                ExecutionId = executionId,
                Sequence = i,
            });
        }

        var missing = _service.GetMissingEvents(executionId, 3);
        Assert.Equal(2, missing.Count);
        Assert.Equal(4, missing[0].Sequence);
        Assert.Equal(5, missing[1].Sequence);
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
