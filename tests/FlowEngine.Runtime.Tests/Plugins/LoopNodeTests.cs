using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// LoopNode 单元测试，重点验证 BatchSize <= 0 时边界校验不崩溃。
/// </summary>
public sealed class LoopNodeTests
{
    private static NodeExecutionContext CreateContext(DataBatch input, IReadOnlyDictionary<string, object>? resolvedParameters = null)
    {
        return new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "test" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = "loop1",
                TypeName = "loop",
                Name = "loop1"
            },
            Inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
            {
                [FlowConstants.PortNames.Input] = input
            },
            ResolvedParameters = resolvedParameters ?? new Dictionary<string, object>()
        };
    }

    private static DataBatch BuildBatch(int count)
    {
        var items = new List<DataItem>();
        for (var i = 0; i < count; i++)
        {
            items.Add(new DataItem
            {
                Data = new JsonObject { ["index"] = i },
                Success = true,
                SourceIndex = i
            });
        }
        return new DataBatch { Items = items };
    }

    [Fact]
    public async Task ExecuteAsync_BatchSizeZero_ClampsToOne()
    {
        var input = BuildBatch(3);
        var node = new LoopNode { BatchSize = 0 };

        var result = await node.ExecuteAsync(CreateContext(input), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        // With BatchSize clamped to 1, first batch should have 1 item
        Assert.Single(result.Output.Items);
        // Routed to loop port (index 0)
        Assert.Equal(0, result.BranchIndex);
    }

    [Fact]
    public async Task ExecuteAsync_BatchSizeNegative_ClampsToOne()
    {
        var input = BuildBatch(3);
        var node = new LoopNode { BatchSize = -5 };

        var result = await node.ExecuteAsync(CreateContext(input), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
        Assert.Equal(0, result.BranchIndex);
    }

    [Fact]
    public async Task ExecuteAsync_BatchSizeOne_ReturnsFirstItem()
    {
        var input = BuildBatch(5);
        var node = new LoopNode { BatchSize = 1 };

        var result = await node.ExecuteAsync(CreateContext(input), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(result.Output.Items);
        Assert.Equal(0, result.Output.Items[0].SourceIndex);
    }

    [Fact]
    public async Task ExecuteAsync_BatchSizeLargerThanInput_ReturnsAllItems()
    {
        var input = BuildBatch(3);
        var node = new LoopNode { BatchSize = 10 };

        var result = await node.ExecuteAsync(CreateContext(input), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(3, result.Output.Items.Count);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyInput_ReturnsDone()
    {
        var input = new DataBatch { Items = [] };
        var node = new LoopNode { BatchSize = 0 };

        var result = await node.ExecuteAsync(CreateContext(input), CancellationToken.None);

        Assert.True(result.Success);
        // Empty input should route to done port (index 1)
        Assert.Equal(1, result.BranchIndex);
    }
}
