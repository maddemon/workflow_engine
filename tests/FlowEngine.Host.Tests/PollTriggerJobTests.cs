using FlowEngine.Application.Triggers;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using Xunit;

namespace FlowEngine.Host.Tests;

public class PollDeduplicationTests
{
    [Fact]
    public void ShouldProcess_NoneStrategy_AlwaysReturnsTrue()
    {
        var item = CreateDataItem("1", DateTime.UtcNow);
        var result = PollDeduplication.ShouldProcess(item, "None", null, null);
        Assert.True(result);
    }

    [Fact]
    public void ShouldProcess_EmptyStrategy_AlwaysReturnsTrue()
    {
        var item = CreateDataItem("1", DateTime.UtcNow);
        var result = PollDeduplication.ShouldProcess(item, "", null, null);
        Assert.True(result);
    }

    [Fact]
    public void ShouldProcess_IdStrategy_NoLastPollId_ReturnsTrue()
    {
        var item = CreateDataItem("1", DateTime.UtcNow);
        var result = PollDeduplication.ShouldProcess(item, "Id", null, null);
        Assert.True(result);
    }

    [Fact]
    public void ShouldProcess_IdStrategy_HigherId_ReturnsTrue()
    {
        var item = CreateDataItem("2", DateTime.UtcNow);
        var result = PollDeduplication.ShouldProcess(item, "Id", "1", null);
        Assert.True(result);
    }

    [Fact]
    public void ShouldProcess_IdStrategy_LowerId_ReturnsFalse()
    {
        var item = CreateDataItem("1", DateTime.UtcNow);
        var result = PollDeduplication.ShouldProcess(item, "Id", "2", null);
        Assert.False(result);
    }

    [Fact]
    public void ShouldProcess_TimestampStrategy_NoLastPollTime_ReturnsTrue()
    {
        var item = CreateDataItem("1", DateTime.UtcNow);
        var result = PollDeduplication.ShouldProcess(item, "Timestamp", null, null);
        Assert.True(result);
    }

    [Fact]
    public void ShouldProcess_TimestampStrategy_LaterTimestamp_ReturnsTrue()
    {
        var now = DateTime.UtcNow;
        var item = CreateDataItem("1", now.AddMinutes(1));
        var result = PollDeduplication.ShouldProcess(item, "Timestamp", null, now);
        Assert.True(result);
    }

    [Fact]
    public void ShouldProcess_TimestampStrategy_EarlierTimestamp_ReturnsFalse()
    {
        var now = DateTime.UtcNow;
        var item = CreateDataItem("1", now.AddMinutes(-1));
        var result = PollDeduplication.ShouldProcess(item, "Timestamp", null, now);
        Assert.False(result);
    }

    [Fact]
    public void ShouldProcess_HashSetStrategy_AlwaysReturnsTrue()
    {
        var item = CreateDataItem("1", DateTime.UtcNow);
        var result = PollDeduplication.ShouldProcess(item, "HashSet", null, null);
        Assert.True(result);
    }

    [Fact]
    public void ShouldProcess_NullData_ReturnsFalse()
    {
        var item = new DataItem { Data = null };
        var result = PollDeduplication.ShouldProcess(item, "Id", null, null);
        Assert.False(result);
    }

    [Fact]
    public void UpdateState_NoneStrategy_ReturnsUnchanged()
    {
        var settings = new TriggerSettings { DedupStrategy = "None" };
        var items = new List<DataItem> { CreateDataItem("1", DateTime.UtcNow) };

        var result = PollDeduplication.UpdateState(items, settings);

        Assert.Null(result.LastPollId);
        Assert.Null(result.LastPollTime);
    }

    [Fact]
    public void UpdateState_IdStrategy_UpdatesLastPollId()
    {
        var settings = new TriggerSettings { DedupStrategy = "Id" };
        var items = new List<DataItem> { CreateDataItem("123", DateTime.UtcNow) };

        var result = PollDeduplication.UpdateState(items, settings);

        Assert.Equal("123", result.LastPollId);
        Assert.NotNull(result.LastPollTime);
    }

    [Fact]
    public void UpdateState_TimestampStrategy_UpdatesLastPollTime()
    {
        var settings = new TriggerSettings { DedupStrategy = "Timestamp" };
        var timestamp = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var items = new List<DataItem> { CreateDataItem("1", timestamp) };

        var result = PollDeduplication.UpdateState(items, settings);

        Assert.Equal(timestamp, result.LastPollTime);
    }

    [Fact]
    public void UpdateState_EmptyItems_ReturnsUnchanged()
    {
        var settings = new TriggerSettings { DedupStrategy = "Id" };
        var items = new List<DataItem>();

        var result = PollDeduplication.UpdateState(items, settings);

        Assert.Null(result.LastPollId);
        Assert.Null(result.LastPollTime);
    }

    private static DataItem CreateDataItem(string id, DateTime timestamp)
    {
        var data = new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = id,
            ["timestamp"] = timestamp.ToString("O"),
        };

        return new DataItem
        {
            Data = data,
            Success = true,
            SourceIndex = 0,
        };
    }
}
