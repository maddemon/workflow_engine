using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// DateTimeNode（N02）测试：覆盖五种运算及错误路径。
/// 沿用 AggregateNodeTests 的模式：用占位节点构建上下文，再用独立配置的节点执行，
/// 避免参数水合（ParameterHydrator）覆盖手动设置的节点属性。
/// </summary>
public sealed class DateTimeNodeTests
{
    [Fact]
    public async Task Now_ReturnsTimestamp()
    {
        var node = new DateTimeNode { Operation = DateTimeOperation.Now };
        var context = await NodeTestContextFactory.BuildAsync(new DateTimeNode(), new Dictionary<string, object>());

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.True(data["timestamp"]?.GetValue<long>() > 0);
        Assert.False(string.IsNullOrEmpty(data["value"]?.GetValue<string>()));
    }

    [Fact]
    public async Task Format_KnownDate_FormatsCorrectly()
    {
        var node = new DateTimeNode
        {
            Operation = DateTimeOperation.Format,
            Input = "2021-01-01T00:00:00Z",
            Format = "yyyy-MM-dd"
        };
        var context = await NodeTestContextFactory.BuildAsync(new DateTimeNode(), new Dictionary<string, object>());

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal("2021-01-01", data["value"]?.GetValue<string>());
        Assert.Equal(1609459200000L, data["timestamp"]?.GetValue<long>());
    }

    [Fact]
    public async Task Add_Days_AddsCorrectly()
    {
        var node = new DateTimeNode
        {
            Operation = DateTimeOperation.Add,
            Input = "2021-01-01T00:00:00Z",
            AddUnit = DateTimeUnit.Day,
            AddValue = 5,
            Format = "yyyy-MM-dd"
        };
        var context = await NodeTestContextFactory.BuildAsync(new DateTimeNode(), new Dictionary<string, object>());

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal("2021-01-06", data["value"]?.GetValue<string>());
    }

    [Fact]
    public async Task Diff_ReturnsMilliseconds()
    {
        var node = new DateTimeNode
        {
            Operation = DateTimeOperation.Diff,
            Input = "2021-01-01T00:00:00Z",
            SecondInput = "2021-01-04T00:00:00Z"
        };
        var context = await NodeTestContextFactory.BuildAsync(new DateTimeNode(), new Dictionary<string, object>());

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal(259200000L, data["timestamp"]?.GetValue<long>());
        Assert.False(string.IsNullOrEmpty(data["value"]?.GetValue<string>()));
    }

    [Fact]
    public async Task ConvertTz_ConvertsCorrectly()
    {
        var node = new DateTimeNode
        {
            Operation = DateTimeOperation.ConvertTz,
            Input = "2021-01-01T00:00:00Z",
            Timezone = "America/New_York",
            Format = "yyyy-MM-dd HH:mm:ss"
        };
        var context = await NodeTestContextFactory.BuildAsync(new DateTimeNode(), new Dictionary<string, object>());

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal("2020-12-31 19:00:00", data["value"]?.GetValue<string>());
        Assert.Equal(1609459200000L, data["timestamp"]?.GetValue<long>());
    }

    [Fact]
    public async Task InvalidFormat_ReturnsErrorResult()
    {
        var node = new DateTimeNode
        {
            Operation = DateTimeOperation.Format,
            Input = "2021-01-01T00:00:00Z",
            Format = "Z"
        };
        var context = await NodeTestContextFactory.BuildAsync(new DateTimeNode(), new Dictionary<string, object>());

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("InvalidFormat", result.Error?.Code);
    }

    [Fact]
    public async Task InvalidTimezone_ReturnsErrorResult()
    {
        var node = new DateTimeNode
        {
            Operation = DateTimeOperation.ConvertTz,
            Input = "2021-01-01T00:00:00Z",
            Timezone = "Not/AReal_Timezone_X"
        };
        var context = await NodeTestContextFactory.BuildAsync(new DateTimeNode(), new Dictionary<string, object>());

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("InvalidTimezone", result.Error?.Code);
    }

    [Fact]
    public async Task UnknownOperation_ReturnsErrorResult()
    {
        var node = new DateTimeNode { Operation = (DateTimeOperation)999 };
        var context = await NodeTestContextFactory.BuildAsync(new DateTimeNode(), new Dictionary<string, object>());

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("UnknownOperation", result.Error?.Code);
    }

    [Fact]
    public async Task UnparseableInput_ReturnsErrorResult()
    {
        var node = new DateTimeNode
        {
            Operation = DateTimeOperation.Format,
            Input = "not-a-date"
        };
        var context = await NodeTestContextFactory.BuildAsync(new DateTimeNode(), new Dictionary<string, object>());

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("InvalidInput", result.Error?.Code);
    }
}
