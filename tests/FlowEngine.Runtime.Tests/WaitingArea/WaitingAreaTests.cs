using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using WaitingAreaType = FlowEngine.Runtime.WaitingArea.WaitingArea;

namespace FlowEngine.Runtime.Tests;

/// <summary>
/// 等待区行为测试：覆盖入队/就绪判定/出队/取消/超时/清理及同端口合并语义。
/// </summary>
public class WaitingAreaTests
{
    private static DataBatch Batch(int itemCount)
    {
        var batch = new DataBatch();
        for (var i = 0; i < itemCount; i++)
        {
            batch.Items.Add(new DataItem
            {
                Data = JsonNode.Parse("1"),
                Success = true,
                SourceIndex = i
            });
        }

        return batch;
    }

    [Fact]
    public void Receive_SinglePort_Then_TryTake_ReturnsCollectedInputs()
    {
        var area = new WaitingAreaType();
        var executionId = Guid.NewGuid();

        area.Receive(executionId, "n1", "in", Batch(2));

        Assert.True(area.TryTake(executionId, "n1", out var inputs));
        Assert.True(inputs.ContainsKey("in"));
        Assert.Equal(2, inputs["in"].Items.Count);
    }

    [Fact]
    public void Receive_AllRequiredPortsPresent_IsReady_ReturnsTrue()
    {
        var area = new WaitingAreaType();
        var executionId = Guid.NewGuid();

        area.Receive(executionId, "n1", "a", Batch(1));
        area.Receive(executionId, "n1", "b", Batch(1));

        Assert.True(area.IsReady(executionId, "n1", new[] { "a", "b" }));
    }

    [Fact]
    public void IsReady_MissingRequiredPort_ReturnsFalse()
    {
        var area = new WaitingAreaType();
        var executionId = Guid.NewGuid();

        area.Receive(executionId, "n1", "a", Batch(1));

        Assert.False(area.IsReady(executionId, "n1", new[] { "a", "b" }));
    }

    [Fact]
    public void IsReady_UnknownNode_ReturnsFalse()
    {
        var area = new WaitingAreaType();

        Assert.False(area.IsReady(Guid.NewGuid(), "nope", new[] { "a" }));
    }

    [Fact]
    public void TryTake_UnknownNode_ReturnsFalse_AndEmptyDictionary()
    {
        var area = new WaitingAreaType();

        Assert.False(area.TryTake(Guid.NewGuid(), "nope", out var inputs));
        Assert.Empty(inputs);
    }

    [Fact]
    public void TryTake_RemovesState_So_Subsequent_Take_ReturnsFalse()
    {
        var area = new WaitingAreaType();
        var executionId = Guid.NewGuid();

        area.Receive(executionId, "n1", "in", Batch(1));

        Assert.True(area.TryTake(executionId, "n1", out _));
        Assert.False(area.TryTake(executionId, "n1", out _));
        Assert.False(area.IsReady(executionId, "n1", new[] { "in" }));
    }

    [Fact]
    public void CancelWaiting_RemovesState_So_NotReady()
    {
        var area = new WaitingAreaType();
        var executionId = Guid.NewGuid();

        area.Receive(executionId, "n1", "in", Batch(1));
        area.CancelWaiting(executionId, "n1");

        Assert.False(area.IsReady(executionId, "n1", new[] { "in" }));
        Assert.True(area.IsEmpty);
    }

    [Fact]
    public void Receive_SamePortTwice_MergesItems_PreservingSequentialSourceIndex()
    {
        var area = new WaitingAreaType();
        var executionId = Guid.NewGuid();

        area.Receive(executionId, "n1", "in", Batch(2));
        area.Receive(executionId, "n1", "in", Batch(3));

        Assert.True(area.TryTake(executionId, "n1", out var inputs));
        var items = inputs["in"].Items;

        // 合并：原有 2 项 + 新到 3 项 = 5 项，且 SourceIndex 重新连续编号。
        Assert.Equal(5, items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            Assert.Equal(i, items[i].SourceIndex);
        }
    }

    [Fact]
    public void Receive_CaseInsensitivePortNames_MergesAcrossCasing()
    {
        var area = new WaitingAreaType();
        var executionId = Guid.NewGuid();

        area.Receive(executionId, "n1", "Input", Batch(1));
        area.Receive(executionId, "n1", "input", Batch(1));

        Assert.True(area.IsReady(executionId, "n1", new[] { "INPUT" }));
        Assert.True(area.TryTake(executionId, "n1", out var inputs));
        Assert.Single(inputs); // 大小写不敏感，合并为同一端口
        Assert.Equal(2, inputs["input"].Items.Count);
    }

    [Fact]
    public void IsEmpty_ReflectsState_AcrossReceiveAndTake()
    {
        var area = new WaitingAreaType();
        var executionId = Guid.NewGuid();

        Assert.True(area.IsEmpty);

        area.Receive(executionId, "n1", "in", Batch(1));
        Assert.False(area.IsEmpty);

        area.TryTake(executionId, "n1", out _);
        Assert.True(area.IsEmpty);
    }

    [Fact]
    public void GetTimeoutKeys_FreshlyReceived_WithLongTimeout_NotTimedOut()
    {
        // 长超时（1 小时）：刚收到必然尚未超时，此断言与墙钟无关，完全确定。
        var area = new WaitingAreaType(TimeSpan.FromHours(1));
        var executionId = Guid.NewGuid();

        area.Receive(executionId, "n1", "in", Batch(1));

        Assert.Empty(area.GetTimeoutKeys());
    }

    [Fact]
    public async Task GetTimeoutKeys_ExceedingTimeout_AppearsInKeys()
    {
        // 1ms 超时：等待 50ms（远大于 1ms）后必然判为超时。
        var area = new WaitingAreaType(TimeSpan.FromMilliseconds(1));
        var executionId = Guid.NewGuid();

        area.Receive(executionId, "n1", "in", Batch(1));

        await Task.Delay(50);

        var keys = area.GetTimeoutKeys().ToList();
        Assert.Contains((executionId, "n1"), keys);
    }

    [Fact]
    public void CleanupExecution_RemovesOnlyThatExecution_LeavingOthers()
    {
        var area = new WaitingAreaType();
        var execA = Guid.NewGuid();
        var execB = Guid.NewGuid();

        area.Receive(execA, "n1", "in", Batch(1));
        area.Receive(execB, "n2", "in", Batch(1));

        area.CleanupExecution(execA);

        Assert.False(area.IsReady(execA, "n1", new[] { "in" }));
        Assert.True(area.IsReady(execB, "n2", new[] { "in" }));
        Assert.False(area.IsEmpty);
    }
}
