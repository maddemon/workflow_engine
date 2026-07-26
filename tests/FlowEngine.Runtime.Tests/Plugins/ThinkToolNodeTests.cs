using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// ThinkToolNode 单元测试，验证从多种输入字段提取 thought 与缺失场景。
/// </summary>
public sealed class ThinkToolNodeTests
{
    private static async Task<NodeExecutionContext> BuildContextAsync(JsonNode? inputData, Dictionary<string, object>? parameters = null)
    {
        var items = inputData is null
            ? new List<DataItem>()
            : new List<DataItem> { new() { Data = inputData, Success = true, SourceIndex = 0 } };

        return await NodeTestContextFactory.BuildAsync(
            new ThinkToolNode(),
            parameters: parameters,
            inputs: new Dictionary<string, DataBatch>
            {
                [FlowConstants.PortNames.Input] = new DataBatch { Items = items }
            }).ConfigureAwait(false);
    }

    [Fact]
    public async Task ExecuteAsync_ThoughtProperty_ReturnsThought()
    {
        var context = await BuildContextAsync(JsonNode.Parse("""{"thought":"hello world"}"""));

        var result = await ((INodeType)new ThinkToolNode()).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("hello world", result.Output.Items[0].Data?["thought"]?.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_ThinkingProperty_FallsBack()
    {
        var context = await BuildContextAsync(JsonNode.Parse("""{"thinking":"thinking deeply"}"""));

        var result = await ((INodeType)new ThinkToolNode()).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("thinking deeply", result.Output.Items[0].Data?["thought"]?.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_ContentProperty_FallsBack()
    {
        var context = await BuildContextAsync(JsonNode.Parse("""{"content":"some content"}"""));

        var result = await ((INodeType)new ThinkToolNode()).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("some content", result.Output.Items[0].Data?["thought"]?.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_InputProperty_FallsBack()
    {
        var context = await BuildContextAsync(JsonNode.Parse("""{"input":"raw input"}"""));

        var result = await ((INodeType)new ThinkToolNode()).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("raw input", result.Output.Items[0].Data?["thought"]?.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_ScalarInput_UsesValue()
    {
        var context = await BuildContextAsync(JsonValue.Create("scalar thought"));

        var result = await ((INodeType)new ThinkToolNode()).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("scalar thought", result.Output.Items[0].Data?["thought"]?.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_ThoughtProperty_FallsBackWhenNoInput()
    {
        var context = await BuildContextAsync(null);

        var node = new ThinkToolNode { Thought = "property thought" };
        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("property thought", result.Output.Items[0].Data?["thought"]?.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_MissingThought_ReturnsError()
    {
        var context = await BuildContextAsync(JsonNode.Parse("{}"));

        var result = await ((INodeType)new ThinkToolNode()).ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingThought", result.Error?.Code);
    }
}
