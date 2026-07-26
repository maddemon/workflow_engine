using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

using FlowEngine.Core.Abstractions;
namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// AggregateNode 单元测试，验证 Concatenate 与 GroupBy 模式。
/// </summary>
public sealed class AggregateNodeTests
{
    private static async Task<NodeExecutionContext> BuildContextAsync(params JsonObject[] rows)
    {
        var items = rows.Select((r, i) => new DataItem { Data = r, Success = true, SourceIndex = i }).ToList();
        return await NodeTestContextFactory.BuildAsync(
            new AggregateNode(),
            inputs: new Dictionary<string, DataBatch>
            {
                [FlowConstants.PortNames.Input] = new DataBatch { Items = items }
            }).ConfigureAwait(false);
    }

    [Fact]
    public async Task ExecuteAsync_Concatenate_AggregatesItemsIntoArray()
    {
        var context = await BuildContextAsync(
            new JsonObject { ["id"] = 1 },
            new JsonObject { ["id"] = 2 });

        var result = await ((INodeType)new AggregateNode { Mode = AggregateMode.Concatenate, OutputFieldName = "items" }).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
        var data = result.Output.Items[0].Data as JsonObject;
        Assert.NotNull(data);
        Assert.Equal(2, data["items"]!.AsArray().Count);
        Assert.Equal(2, data["count"]!.GetValue<int>());
    }

    [Fact]
    public async Task ExecuteAsync_GroupBy_AggregatesByField()
    {
        var context = await BuildContextAsync(
            new JsonObject { ["group"] = "a", ["v"] = 1 },
            new JsonObject { ["group"] = "a", ["v"] = 2 },
            new JsonObject { ["group"] = "b", ["v"] = 3 });

        var result = await ((INodeType)new AggregateNode { Mode = AggregateMode.GroupBy, GroupByField = "group", OutputFieldName = "items" }).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
        var groupA = result.Output.Items.First(i => i.Data!["group"]!.GetValue<string>() == "a");
        Assert.Equal(2, groupA.Data!["items"]!.AsArray().Count);
        Assert.Equal(2, groupA.Data["count"]!.GetValue<int>());
    }

    [Fact]
    public async Task ExecuteAsync_GroupByEmptyField_FallsBackToConcatenate()
    {
        var context = await BuildContextAsync(
            new JsonObject { ["id"] = 1 },
            new JsonObject { ["id"] = 2 });

        var result = await ((INodeType)new AggregateNode { Mode = AggregateMode.GroupBy, GroupByField = "", OutputFieldName = "items" }).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
        Assert.Equal(2, result.Output.Items[0].Data!["items"]!.AsArray().Count);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyInput_ReturnsEmptyItems()
    {
        var context = await BuildContextAsync();

        var result = await ((INodeType)new AggregateNode { Mode = AggregateMode.Concatenate }).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
        var data = result.Output.Items[0].Data as JsonObject;
        Assert.NotNull(data);
        Assert.Empty(data["items"]!.AsArray());
        Assert.Equal(0, data["count"]!.GetValue<int>());
    }
}
