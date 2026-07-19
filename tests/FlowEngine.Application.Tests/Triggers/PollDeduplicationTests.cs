using System.Text.Json.Nodes;
using FlowEngine.Application.Triggers;
using FlowEngine.Core.Entities;
using Xunit;

namespace FlowEngine.Application.Tests.Triggers;

/// <summary>
/// PollDeduplication 静态方法测试，覆盖 ShouldProcess / UpdateState 全部分支。
/// </summary>
public sealed class PollDeduplicationTests
{
    [Fact]
    public void ShouldProcess_NoneOrEmpty_ReturnsTrue()
    {
        var item = new DataItem { Data = JsonNode.Parse("""{"id":"1"}""") };

        Assert.True(PollDeduplication.ShouldProcess(item, "None", null, null));
        Assert.True(PollDeduplication.ShouldProcess(item, "", null, null));
    }

    [Fact]
    public void ShouldProcess_NullData_ReturnsFalse()
    {
        var item = new DataItem { Data = null };

        Assert.False(PollDeduplication.ShouldProcess(item, "id", null, null));
    }

    [Fact]
    public void ShouldProcess_UnknownStrategy_ReturnsTrue()
    {
        var item = new DataItem { Data = JsonNode.Parse("""{"id":"1"}""") };

        Assert.True(PollDeduplication.ShouldProcess(item, "magic", null, null));
    }

    [Fact]
    public void ShouldProcess_ById_VariousScenarios()
    {
        var itemWithId = new DataItem { Data = JsonNode.Parse("""{"id":"b"}""") };
        var itemWithoutId = new DataItem { Data = JsonNode.Parse("""{"name":"x"}""") };
        var nonObject = new DataItem { Data = JsonValue.Create("string") };

        Assert.True(PollDeduplication.ShouldProcess(itemWithId, "id", "a", null));
        Assert.False(PollDeduplication.ShouldProcess(itemWithId, "id", "b", null));
        Assert.False(PollDeduplication.ShouldProcess(itemWithId, "id", "c", null));

        Assert.True(PollDeduplication.ShouldProcess(itemWithoutId, "id", "a", null));
        Assert.True(PollDeduplication.ShouldProcess(nonObject, "id", "a", null));

        Assert.True(PollDeduplication.ShouldProcess(itemWithId, "id", "", null));
        Assert.True(PollDeduplication.ShouldProcess(new DataItem { Data = JsonNode.Parse("""{"id":""}""") }, "id", "a", null));
    }

    [Fact]
    public void ShouldProcess_ByTimestamp_VariousScenarios()
    {
        var baseTime = new DateTime(2026, 7, 19, 10, 0, 0, DateTimeKind.Utc);
        var newer = new DataItem { Data = JsonNode.Parse("""{"timestamp":"2026-07-19T10:00:01Z"}""") };
        var older = new DataItem { Data = JsonNode.Parse("""{"timestamp":"2026-07-19T09:59:59Z"}""") };
        var noTimestamp = new DataItem { Data = JsonNode.Parse("""{"id":"1"}""") };
        var nonObject = new DataItem { Data = JsonValue.Create(42) };
        var unparsable = new DataItem { Data = JsonNode.Parse("""{"timestamp":"not-a-date"}""") };

        Assert.True(PollDeduplication.ShouldProcess(newer, "timestamp", null, baseTime));
        Assert.False(PollDeduplication.ShouldProcess(older, "timestamp", null, baseTime));
        Assert.True(PollDeduplication.ShouldProcess(noTimestamp, "timestamp", null, baseTime));
        Assert.True(PollDeduplication.ShouldProcess(nonObject, "timestamp", null, baseTime));
        Assert.True(PollDeduplication.ShouldProcess(unparsable, "timestamp", null, baseTime));
        Assert.True(PollDeduplication.ShouldProcess(newer, "timestamp", null, null));
    }

    [Fact]
    public void ShouldProcess_ByHashSet_VariousScenarios()
    {
        var itemA = new DataItem { Data = JsonNode.Parse("""{"id":"a"}""") };
        var itemB = new DataItem { Data = JsonNode.Parse("""{"id":"b"}""") };
        var noId = new DataItem { Data = JsonNode.Parse("""{"name":"x"}""") };
        var invalidJson = "not-json";

        Assert.True(PollDeduplication.ShouldProcess(itemA, "hashset", invalidJson, null));
        Assert.False(PollDeduplication.ShouldProcess(itemA, "hashset", """["id:a"]""", null));
        Assert.True(PollDeduplication.ShouldProcess(itemB, "hashset", """["id:a"]""", null));
        Assert.True(PollDeduplication.ShouldProcess(noId, "hashset", """["id:a"]""", null));
    }

    [Fact]
    public void UpdateState_EmptyItems_ReturnsOriginalSettings()
    {
        var settings = new TriggerSettings { DedupStrategy = "id", LastPollId = "a" };

        var result = PollDeduplication.UpdateState([], settings);

        Assert.Same(settings, result);
    }

    [Fact]
    public void UpdateState_NoneStrategy_ReturnsOriginalSettings()
    {
        var settings = new TriggerSettings { DedupStrategy = "None" };
        var items = new List<DataItem> { new() { Data = JsonNode.Parse("""{"id":"1"}""") } };

        var result = PollDeduplication.UpdateState(items, settings);

        Assert.Same(settings, result);
    }

    [Fact]
    public void UpdateState_Id_SetsLastPollId()
    {
        var settings = new TriggerSettings { DedupStrategy = "id" };
        var items = new List<DataItem>
        {
            new() { Data = JsonNode.Parse("""{"id":"1"}""") },
            new() { Data = JsonNode.Parse("""{"id":"2"}""") },
        };

        var result = PollDeduplication.UpdateState(items, settings);

        Assert.Equal("2", result.LastPollId);
        Assert.NotNull(result.LastPollTime);
    }

    [Fact]
    public void UpdateState_Timestamp_SetsLastPollTime()
    {
        var settings = new TriggerSettings { DedupStrategy = "timestamp" };
        var items = new List<DataItem>
        {
            new() { Data = JsonNode.Parse("""{"timestamp":"2026-07-19T12:00:00Z"}""") },
        };

        var result = PollDeduplication.UpdateState(items, settings);

        Assert.Equal(new DateTime(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc), result.LastPollTime!.Value.ToUniversalTime());
    }

    [Fact]
    public void UpdateState_Timestamp_InvalidParse_KeepsLastPollTime()
    {
        var settings = new TriggerSettings { DedupStrategy = "timestamp", LastPollTime = DateTime.UtcNow };
        var items = new List<DataItem>
        {
            new() { Data = JsonNode.Parse("""{"timestamp":"bad"}""") },
        };

        var result = PollDeduplication.UpdateState(items, settings);

        Assert.Equal(settings.LastPollTime, result.LastPollTime);
    }

    [Fact]
    public void UpdateState_HashSet_AppendsKeysAndDeduplicates()
    {
        var settings = new TriggerSettings { DedupStrategy = "hashset", LastPollId = """["id:a"]""" };
        var items = new List<DataItem>
        {
            new() { Data = JsonNode.Parse("""{"id":"a"}""") },
            new() { Data = JsonNode.Parse("""{"id":"b"}""") },
            new() { Data = JsonNode.Parse("""{"id":"b"}""") },
        };

        var result = PollDeduplication.UpdateState(items, settings);

        var arr = JsonArray.Parse(result.LastPollId!) as JsonArray;
        Assert.NotNull(arr);
        Assert.Equal(2, arr.Count);
        Assert.Contains("id:a", arr.Select(n => n!.ToString()));
        Assert.Contains("id:b", arr.Select(n => n!.ToString()));
        Assert.NotNull(result.LastPollTime);
    }
}
