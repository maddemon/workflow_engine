using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// noOp 节点测试：覆盖透传语义（输入批次 N 条 → 输出相同 N 条且 Data 相等）、无输入返回空批次、端口与契约。
/// </summary>
public sealed class NoOpNodeTests
{
    [Fact]
    public async Task ExecuteAsync_Passthrough_OutputSameCountWithEqualData()
    {
        var node = new NoOpNode();

        var inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
        {
            [FlowConstants.PortNames.Input] = new DataBatch
            {
                Items =
                [
                    new DataItem { Data = JsonNode.Parse("""{"id":1,"name":"a"}"""), Success = true, SourceIndex = 0 },
                    new DataItem { Data = JsonNode.Parse("""{"id":2,"name":"b"}"""), Success = true, SourceIndex = 1 },
                    new DataItem { Data = JsonNode.Parse("""{"id":3,"name":"c"}"""), Success = true, SourceIndex = 2 }
                ]
            }
        };

        var context = await NodeTestContextFactory.BuildAsync(node, null, inputs);
        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Output);
        Assert.Equal(3, result.Output!.Items.Count);

        Assert.Equal("""{"id":1,"name":"a"}""", result.Output.Items[0].Data!.ToJsonString());
        Assert.Equal("""{"id":2,"name":"b"}""", result.Output.Items[1].Data!.ToJsonString());
        Assert.Equal("""{"id":3,"name":"c"}""", result.Output.Items[2].Data!.ToJsonString());
    }

    [Fact]
    public async Task ExecuteAsync_EmptyInput_ReturnsEmptyBatchWithoutError()
    {
        var node = new NoOpNode();

        var inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
        {
            [FlowConstants.PortNames.Input] = new DataBatch { Items = [] }
        };

        var context = await NodeTestContextFactory.BuildAsync(node, null, inputs);
        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.NotNull(result.Output);
        Assert.Empty(result.Output!.Items);
    }

    [Fact]
    public async Task ExecuteAsync_MissingInputPort_ReturnsEmptyBatchWithoutError()
    {
        var node = new NoOpNode();

        // 不注入任何 inputs，GetInputBatch 应回退为空批次。
        var context = await NodeTestContextFactory.BuildAsync(node, null, null);
        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.NotNull(result.Output);
        Assert.Empty(result.Output!.Items);
    }

    [Fact]
    public void Ports_ContainsInputAndOutput()
    {
        var node = new NoOpNode();

        Assert.Equal(2, node.Ports.Count);
        Assert.Contains(node.Ports, p => p.Name == FlowConstants.PortNames.Input && p.Direction == PortDirection.Input && p.Type == PortType.Main);
        Assert.Contains(node.Ports, p => p.Name == FlowConstants.PortNames.Output && p.Direction == PortDirection.Output && p.Type == PortType.Main);
    }

    [Fact]
    public void Contract_IsStable_TypeNameCategoryIcon_Unchanged()
    {
        var node = new NoOpNode();

        Assert.Equal("noOp", node.TypeName);
        Assert.Equal("No Op", node.DisplayName);
        Assert.Equal("Flow", node.Category);
        Assert.Equal("noop", node.Icon);
        Assert.Equal(ExecutionMode.OnceForAll, node.ExecutionMode);
        Assert.False(node.DefaultIsEntry);
    }
}
