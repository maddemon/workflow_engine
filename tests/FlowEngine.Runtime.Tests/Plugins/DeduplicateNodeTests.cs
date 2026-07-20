using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// DeduplicateNode 单元测试，验证全项键与字段键、保留首/尾。
/// </summary>
public sealed class DeduplicateNodeTests
{
    private static async Task<NodeExecutionContext> BuildContextAsync(params JsonNode?[] nodes)
    {
        var items = new List<DataItem>();
        for (var i = 0; i < nodes.Length; i++)
        {
            items.Add(new DataItem { Data = nodes[i]?.DeepClone(), Success = true, SourceIndex = i });
        }

        return await NodeTestContextFactory.BuildAsync(
            new DeduplicateNode(),
            inputs: new Dictionary<string, DataBatch>
            {
                [FlowConstants.PortNames.Input] = new DataBatch { Items = items }
            }).ConfigureAwait(false);
    }

    [Fact]
    public async Task ExecuteAsync_KeepFirstWholeItem_KeepsFirstDuplicate()
    {
        var dup = JsonNode.Parse("{\"id\":1}");
        var context = await BuildContextAsync(dup, dup, JsonNode.Parse("{\"id\":2}"));

        var result = await new DeduplicateNode { KeepFirst = true }.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
        Assert.Equal(1, result.Output.Items[0].Data?["id"]?.GetValue<int>());
        Assert.Equal(2, result.Output.Items[1].Data?["id"]?.GetValue<int>());
    }

    [Fact]
    public async Task ExecuteAsync_KeepFirstByField_KeepsFirstMatchingField()
    {
        var context = await BuildContextAsync(
            JsonNode.Parse("{\"group\":\"a\",\"seq\":1}"),
            JsonNode.Parse("{\"group\":\"a\",\"seq\":2}"),
            JsonNode.Parse("{\"group\":\"b\",\"seq\":3}"));

        var result = await new DeduplicateNode { CompareField = "group", KeepFirst = true }.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
        Assert.Equal(1, result.Output.Items[0].Data?["seq"]?.GetValue<int>());
        Assert.Equal(3, result.Output.Items[1].Data?["seq"]?.GetValue<int>());
    }

    [Fact]
    public async Task ExecuteAsync_KeepLastByField_KeepsLastMatchingField()
    {
        var context = await BuildContextAsync(
            JsonNode.Parse("{\"group\":\"a\",\"seq\":1}"),
            JsonNode.Parse("{\"group\":\"a\",\"seq\":2}"),
            JsonNode.Parse("{\"group\":\"b\",\"seq\":3}"));

        var result = await new DeduplicateNode { CompareField = "group", KeepFirst = false }.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
        Assert.Equal(2, result.Output.Items[0].Data?["seq"]?.GetValue<int>());
        Assert.Equal(3, result.Output.Items[1].Data?["seq"]?.GetValue<int>());
    }

    [Fact]
    public async Task ExecuteAsync_NoDuplicates_PassesAll()
    {
        var context = await BuildContextAsync(
            JsonNode.Parse("{\"id\":1}"),
            JsonNode.Parse("{\"id\":2}"),
            JsonNode.Parse("{\"id\":3}"));

        var result = await new DeduplicateNode().ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(3, result.Output.Items.Count);
    }

    [Fact]
    public async Task ExecuteAsync_NullData_TreatsAsSameKey()
    {
        var context = await BuildContextAsync(null, null, JsonNode.Parse("{\"id\":1}"));

        var result = await new DeduplicateNode { KeepFirst = true }.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
    }
}
