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

    [Fact]
    public async Task ExecuteAsync_EmptyInput_ReturnsDoneWithEmptyOutput()
    {
        var input = new DataBatch { Items = [] };
        var node = new LoopNode { BatchSize = 2 };

        var result = await node.ExecuteAsync(CreateContext(input), CancellationToken.None);

        Assert.True(result.Success);
        // 输入为空：走 Done 输出口（BranchIndex = 1）且输出为空批。
        Assert.Equal(1, result.BranchIndex);
        Assert.Empty(result.Output.Items);
    }

    [Fact]
    public async Task ExecuteAsync_BatchSizeTwoWithFiveItems_ReturnsFirstTwoOnLoop()
    {
        var input = BuildBatch(5);
        var node = new LoopNode { BatchSize = 2 };

        var result = await node.ExecuteAsync(CreateContext(input), CancellationToken.None);

        Assert.True(result.Success);
        // 单窗口语义：恰好前 2 项从 Loop 输出口（BranchIndex = 0）发出。
        Assert.Equal(0, result.BranchIndex);
        Assert.Equal(2, result.Output.Items.Count);
        Assert.Equal(0, result.Output.Items[0].SourceIndex);
        Assert.Equal(1, result.Output.Items[1].SourceIndex);
        // 第 3~5 项不在单窗口输出中（文档限制：超出部分不迭代）。
        Assert.DoesNotContain(result.Output.Items, i => i.SourceIndex >= 2);
    }

    [Fact]
    public async Task ExecuteAsync_BatchSizeGreaterThanInput_ReturnsAllItemsOnLoop()
    {
        var input = BuildBatch(3);
        var node = new LoopNode { BatchSize = 10 };

        var result = await node.ExecuteAsync(CreateContext(input), CancellationToken.None);

        Assert.True(result.Success);
        // 批次大于输入数：全部项从 Loop 输出口（BranchIndex = 0）发出。
        Assert.Equal(0, result.BranchIndex);
        Assert.Equal(3, result.Output.Items.Count);
    }

    [Fact]
    public async Task ExecuteAsync_NextBatchPositionContractIgnored_ReturnsFirstWindow()
    {
        // 执行内核从不回灌 nextBatch/position；即便显式传入，单窗口语义也只发首批，
        // 不按 position 推进到后续批次。旧代码的迭代契约会据此返回 items 2..3（死路径）。
        var input = BuildBatch(5);
        var node = new LoopNode { BatchSize = 2 };
        var resolved = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["nextBatch"] = JsonValue.Create(true),
            ["position"] = JsonValue.Create(2)
        };

        var result = await node.ExecuteAsync(CreateContext(input, resolved), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, result.BranchIndex); // Loop
        // 仍只发首批（items 0..1），而非 items 2..3。
        Assert.Equal(2, result.Output.Items.Count);
        Assert.Equal(0, result.Output.Items[0].SourceIndex);
        Assert.Equal(1, result.Output.Items[1].SourceIndex);
    }
}
