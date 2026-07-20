using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// LimitNode 单元测试，验证 Skip / Take 行为及边界场景。
/// </summary>
public sealed class LimitNodeTests
{
    private static async Task<NodeExecutionContext> BuildContextAsync(params int[] values)
    {
        var items = new List<DataItem>();
        for (var i = 0; i < values.Length; i++)
        {
            items.Add(new DataItem
            {
                Data = JsonNode.Parse($"{{\"value\":{values[i]}}}"),
                Success = true,
                SourceIndex = i
            });
        }

        return await NodeTestContextFactory.BuildAsync(
            new LimitNode(),
            inputs: new Dictionary<string, DataBatch>
            {
                [FlowConstants.PortNames.Input] = new DataBatch { Items = items }
            }).ConfigureAwait(false);
    }

    [Fact]
    public async Task ExecuteAsync_TakeLessThanCount_ReturnsLimitedItems()
    {
        var context = await BuildContextAsync(1, 2, 3, 4, 5);

        var result = await new LimitNode { MaxItems = 2 }.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
        Assert.Equal(1, result.Output.Items[0].Data?["value"]?.GetValue<int>());
        Assert.Equal(2, result.Output.Items[1].Data?["value"]?.GetValue<int>());
    }

    [Fact]
    public async Task ExecuteAsync_SkipsLeadingItems()
    {
        var context = await BuildContextAsync(1, 2, 3, 4, 5);

        var result = await new LimitNode { Skip = 2, MaxItems = 2 }.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
        Assert.Equal(3, result.Output.Items[0].Data?["value"]?.GetValue<int>());
        Assert.Equal(4, result.Output.Items[1].Data?["value"]?.GetValue<int>());
    }

    [Fact]
    public async Task ExecuteAsync_SkipExceedsCount_ReturnsEmpty()
    {
        var context = await BuildContextAsync(1, 2);

        var result = await new LimitNode { Skip = 5 }.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Empty(result.Output.Items);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyInput_ReturnsEmpty()
    {
        var context = await BuildContextAsync();

        var result = await new LimitNode { MaxItems = 10 }.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Empty(result.Output.Items);
    }
}
