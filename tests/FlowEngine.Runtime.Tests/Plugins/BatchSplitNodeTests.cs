using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// BatchSplitNode 单元测试。重点验证：窗口切分/位置推进复用 <see cref="BatchLoopHelper"/>（与 LoopNode 同机制）、
/// Done 输出为固定空批次、回环激活忽略下游回流输入。循环由内核反馈边驱动，此处用持久化 NodeContext 模拟多次回环调用。
/// </summary>
public sealed class BatchSplitNodeTests
{
    private static NodeExecutionContext CreateStatefulContext(DataBatch input, IDictionary<string, object?> nodeContext)
    {
        return new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "test" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = "batchSplit1",
                TypeName = "batchSplit",
                Name = "batchSplit1"
            },
            Inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
            {
                [FlowConstants.PortNames.Input] = input
            },
            ResolvedParameters = new Dictionary<string, object>(),
            NodeContext = nodeContext
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
        var nodeContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var node = new BatchSplitNode { BatchSize = 0 };

        var result = await node.ExecuteAsync(CreateStatefulContext(BuildBatch(3), nodeContext), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        // BatchSize<=0 钳制为 1：首批 1 项，走 Loop 输出口（BranchIndex = 0）。
        Assert.Single(result.Output.Items);
        Assert.Equal(0, result.BranchIndex);
    }

    [Fact]
    public async Task ExecuteAsync_BatchSizeNegative_ClampsToOne()
    {
        var nodeContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var node = new BatchSplitNode { BatchSize = -5 };

        var result = await node.ExecuteAsync(CreateStatefulContext(BuildBatch(3), nodeContext), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
        Assert.Equal(0, result.BranchIndex);
    }

    [Fact]
    public async Task ExecuteAsync_FirstCall_ReturnsFirstWindowOfSizeK()
    {
        var nodeContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var node = new BatchSplitNode { BatchSize = 2 };

        var result = await node.ExecuteAsync(CreateStatefulContext(BuildBatch(5), nodeContext), CancellationToken.None);

        Assert.True(result.Success);
        // 首批 K=2 项从 Loop 输出口（BranchIndex = 0）发出。
        Assert.Equal(0, result.BranchIndex);
        Assert.Equal(2, result.Output.Items.Count);
        Assert.Equal(0, result.Output.Items[0].SourceIndex);
        Assert.Equal(1, result.Output.Items[1].SourceIndex);
    }

    [Fact]
    public async Task ExecuteAsync_FirstCall_WindowClampedToItemCount()
    {
        var nodeContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var node = new BatchSplitNode { BatchSize = 10 };

        var result = await node.ExecuteAsync(CreateStatefulContext(BuildBatch(3), nodeContext), CancellationToken.None);

        Assert.True(result.Success);
        // 批次大于输入数：全部 3 项从 Loop 输出口发出。
        Assert.Equal(0, result.BranchIndex);
        Assert.Equal(3, result.Output.Items.Count);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyInput_ReturnsDoneEmpty()
    {
        var nodeContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var node = new BatchSplitNode { BatchSize = 2 };

        var result = await node.ExecuteAsync(CreateStatefulContext(new DataBatch { Items = [] }, nodeContext), CancellationToken.None);

        Assert.True(result.Success);
        // 输入为空：首调即走 Done 输出口（BranchIndex = 1）且输出为空批次。
        Assert.Equal(1, result.BranchIndex);
        Assert.Empty(result.Output.Items);
    }

    [Fact]
    public async Task ExecuteAsync_BatchSizeOne_IteratesItemByItemThenDone()
    {
        var nodeContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var node = new BatchSplitNode { BatchSize = 1 };
        var ct = CancellationToken.None;

        var r1 = await node.ExecuteAsync(CreateStatefulContext(BuildBatch(3), nodeContext), ct);
        Assert.Equal(0, r1.BranchIndex);
        Assert.Single(r1.Output.Items);
        Assert.Equal(0, r1.Output.Items[0].SourceIndex);

        var r2 = await node.ExecuteAsync(CreateStatefulContext(r1.Output, nodeContext), ct);
        Assert.Equal(0, r2.BranchIndex);
        Assert.Single(r2.Output.Items);
        Assert.Equal(1, r2.Output.Items[0].SourceIndex);

        var r3 = await node.ExecuteAsync(CreateStatefulContext(r2.Output, nodeContext), ct);
        Assert.Equal(0, r3.BranchIndex);
        Assert.Single(r3.Output.Items);
        Assert.Equal(2, r3.Output.Items[0].SourceIndex);

        // 第 4 次：全部发完 → Done（空批次）。
        var r4 = await node.ExecuteAsync(CreateStatefulContext(r3.Output, nodeContext), ct);
        Assert.Equal(1, r4.BranchIndex);
        Assert.Empty(r4.Output.Items);
    }

    [Fact]
    public async Task ExecuteAsync_IteratesAcrossCalls_EmitsSuccessiveWindows()
    {
        var nodeContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var node = new BatchSplitNode { BatchSize = 2 };
        var ct = CancellationToken.None;

        // Call 1：发首批 [0,1]（Loop）。
        var r1 = await node.ExecuteAsync(CreateStatefulContext(BuildBatch(5), nodeContext), ct);
        Assert.Equal(0, r1.BranchIndex);
        Assert.Equal(2, r1.Output.Items.Count);

        // Call 2：发第二批 [2,3]（Loop）。
        var r2 = await node.ExecuteAsync(CreateStatefulContext(r1.Output, nodeContext), ct);
        Assert.Equal(0, r2.BranchIndex);
        Assert.Equal(2, r2.Output.Items.Count);
        Assert.Equal(2, r2.Output.Items[0].SourceIndex);
        Assert.Equal(3, r2.Output.Items[1].SourceIndex);

        // Call 3：发末批 [4]（Loop）。
        var r3 = await node.ExecuteAsync(CreateStatefulContext(r2.Output, nodeContext), ct);
        Assert.Equal(0, r3.BranchIndex);
        Assert.Single(r3.Output.Items);
        Assert.Equal(4, r3.Output.Items[0].SourceIndex);

        // Call 4：全部发完 → Done（空批次，不回收回流数据）。
        var r4 = await node.ExecuteAsync(CreateStatefulContext(r3.Output, nodeContext), ct);
        Assert.Equal(1, r4.BranchIndex);
        Assert.Empty(r4.Output.Items);
    }

    [Fact]
    public async Task ExecuteAsync_FeedbackReturnsDifferentItems_Ignored()
    {
        // 回环输入与原始 allItems 不同（下游过滤/重排）时，batchSplit 仍按原始全集切片推进，
        // 不回收、不重置位置；Done 输出为空批次（而非回流数据）。
        var nodeContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var node = new BatchSplitNode { BatchSize = 2 };
        var ct = CancellationToken.None;

        var r1 = await node.ExecuteAsync(CreateStatefulContext(BuildBatch(4), nodeContext), ct);
        Assert.Equal(0, r1.BranchIndex);
        Assert.Equal(2, r1.Output.Items.Count);

        // 回环输入：下游仅回灌 1 个与 allItems 无关的项；节点应忽略，继续发 [2,3]。
        var feedback = new DataBatch { Items = { new DataItem { Data = new JsonObject { ["x"] = "ignored" }, Success = true, SourceIndex = 99 } } };
        var r2 = await node.ExecuteAsync(CreateStatefulContext(feedback, nodeContext), ct);
        Assert.Equal(0, r2.BranchIndex);
        Assert.Equal(2, r2.Output.Items.Count);
        Assert.Equal(2, r2.Output.Items[0].SourceIndex);
        Assert.Equal(3, r2.Output.Items[1].SourceIndex);

        // 回环输入：再回灌无关项；节点忽略 → Done（空批次）。
        var r3 = await node.ExecuteAsync(CreateStatefulContext(feedback, nodeContext), ct);
        Assert.Equal(1, r3.BranchIndex);
        Assert.Empty(r3.Output.Items);
    }

    [Fact]
    public async Task ExecuteAsync_FreshContextEachCall_AlwaysEmitsFirstWindow()
    {
        // 每次调用都用全新上下文（非回边激活），循环从头开始，互不串扰。
        var node = new BatchSplitNode { BatchSize = 2 };
        var ct = CancellationToken.None;

        var r1 = await node.ExecuteAsync(CreateStatefulContext(BuildBatch(5), new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)), ct);
        var r2 = await node.ExecuteAsync(CreateStatefulContext(BuildBatch(5), new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)), ct);

        Assert.Equal(0, r1.BranchIndex);
        Assert.Equal(0, r2.BranchIndex);
        Assert.Equal(2, r1.Output.Items.Count);
        Assert.Equal(2, r2.Output.Items.Count);
        Assert.Equal(0, r1.Output.Items[0].SourceIndex);
        Assert.Equal(0, r2.Output.Items[0].SourceIndex);
    }
}
