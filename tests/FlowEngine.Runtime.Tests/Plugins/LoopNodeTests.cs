using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// LoopNode 单元测试，重点验证 BatchSize &lt;= 0 时边界校验不崩坏、迭代语义与节点上下文累积。
/// 迁移为 NodeBase 后，经 <c>((INodeType)node).ExecuteAsync</c> 走适配层。
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

    private static NodeExecutionContext CreateStatefulContext(DataBatch input, IDictionary<string, object?> nodeContext)
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
            ResolvedParameters = new Dictionary<string, object>(),
            NodeContext = nodeContext
        };
    }

    private static Task<NodeExecutionResult> RunAsync(LoopNode node, NodeExecutionContext context, CancellationToken ct = default)
        => ((INodeType)node).ExecuteAsync(context, ct);

    [Fact]
    public async Task ExecuteAsync_BatchSizeZero_ClampsToOne()
    {
        var input = BuildBatch(3);
        var node = new LoopNode { BatchSize = 0 };

        var result = await RunAsync(node, CreateContext(input), CancellationToken.None);

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

        var result = await RunAsync(node, CreateContext(input), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
        Assert.Equal(0, result.BranchIndex);
    }

    [Fact]
    public async Task ExecuteAsync_BatchSizeOne_ReturnsFirstItem()
    {
        var input = BuildBatch(5);
        var node = new LoopNode { BatchSize = 1 };

        var result = await RunAsync(node, CreateContext(input), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(result.Output.Items);
        Assert.Equal(0, result.Output.Items[0].SourceIndex);
    }

    [Fact]
    public async Task ExecuteAsync_BatchSizeLargerThanInput_ReturnsAllItems()
    {
        var input = BuildBatch(3);
        var node = new LoopNode { BatchSize = 10 };

        var result = await RunAsync(node, CreateContext(input), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(3, result.Output.Items.Count);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyInput_ReturnsDone()
    {
        var input = new DataBatch { Items = [] };
        var node = new LoopNode { BatchSize = 0 };

        var result = await RunAsync(node, CreateContext(input), CancellationToken.None);

        Assert.True(result.Success);
        // Empty input should route to done port (index 1)
        Assert.Equal(1, result.BranchIndex);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyInput_ReturnsDoneWithEmptyOutput()
    {
        var input = new DataBatch { Items = [] };
        var node = new LoopNode { BatchSize = 2 };

        var result = await RunAsync(node, CreateContext(input), CancellationToken.None);

        Assert.True(result.Success);
        // 输入为空：走 Done 输出口（BranchIndex = 1）且输出为空批次。
        Assert.Equal(1, result.BranchIndex);
        Assert.Empty(result.Output.Items);
    }

    [Fact]
    public async Task ExecuteAsync_BatchSizeTwoWithFiveItems_ReturnsFirstTwoOnLoop()
    {
        var input = BuildBatch(5);
        var node = new LoopNode { BatchSize = 2 };

        var result = await RunAsync(node, CreateContext(input), CancellationToken.None);

        Assert.True(result.Success);
        // 单词口语言：首批 2 项从 Loop 输出口（BranchIndex = 0）发出。
        Assert.Equal(0, result.BranchIndex);
        Assert.Equal(2, result.Output.Items.Count);
        Assert.Equal(0, result.Output.Items[0].SourceIndex);
        Assert.Equal(1, result.Output.Items[1].SourceIndex);
        // 第 3~5 项不在单词口输出中（文档限制：超出部分不迭代）。
        Assert.DoesNotContain(result.Output.Items, i => i.SourceIndex >= 2);
    }

    [Fact]
    public async Task ExecuteAsync_BatchSizeGreaterThanInput_ReturnsAllItemsOnLoop()
    {
        var input = BuildBatch(3);
        var node = new LoopNode { BatchSize = 10 };

        var result = await RunAsync(node, CreateContext(input), CancellationToken.None);

        Assert.True(result.Success);
        // 批次大于输入数：全部项从 Loop 输出口（BranchIndex = 0）发出。
        Assert.Equal(0, result.BranchIndex);
        Assert.Equal(3, result.Output.Items.Count);
    }

    [Fact]
    public async Task ExecuteAsync_NextBatchPositionContractIgnored_ReturnsFirstWindow()
    {
        // 执行内核从不回传 nextBatch/position；即便显式传入，单词口语言也只发首批，
        // 不按 position 推进到后续批次。旧代码的错误契约会据此返回 items 2..3（死路径）。
        var input = BuildBatch(5);
        var node = new LoopNode { BatchSize = 2 };
        var resolved = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["nextBatch"] = JsonValue.Create(true),
            ["position"] = JsonValue.Create(2)
        };

        var result = await RunAsync(node, CreateContext(input, resolved), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, result.BranchIndex); // Loop
        // 仍只发首批（items 0..1），而非 items 2..3。
        Assert.Equal(2, result.Output.Items.Count);
        Assert.Equal(0, result.Output.Items[0].SourceIndex);
        Assert.Equal(1, result.Output.Items[1].SourceIndex);
    }

    // Task 6：模拟内核 GetOrAdd —— 同一 NodeContext 实例跨多次调用保持（回环激活复用上下文），
    // 回环时输入为下游处理过的窗口。验证 Loop/Done 端口切换与「累积处理结果」语义。
    [Fact]
    public async Task ExecuteAsync_IteratesAcrossCalls_AccumulatesProcessedResults()
    {
        var nodeContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var node = new LoopNode { BatchSize = 2 };
        var ct = CancellationToken.None;

        // Call 1：初始输入 5 项原始全集 → 发首批（Loop）。
        var r1 = await RunAsync(node, CreateStatefulContext(BuildBatch(5), nodeContext), ct);
        Assert.Equal(0, r1.BranchIndex);
        Assert.Equal(2, r1.Output.Items.Count);

        // Call 2：回环输入 = 上一批的「已处理」结果（此处原样回灌模拟下游）。
        var r2 = await RunAsync(node, CreateStatefulContext(r1.Output, nodeContext), ct);
        Assert.Equal(0, r2.BranchIndex);
        Assert.Equal(2, r2.Output.Items.Count);

        // Call 3：第三批。
        var r3 = await RunAsync(node, CreateStatefulContext(r2.Output, nodeContext), ct);
        Assert.Equal(0, r3.BranchIndex);
        Assert.Single(r3.Output.Items);

        // Call 4：第四批回流 → 全部处理完，走 Done，累积全部 5 项处理结果。
        var r4 = await RunAsync(node, CreateStatefulContext(r3.Output, nodeContext), ct);
        Assert.Equal(1, r4.BranchIndex);
        Assert.Equal(5, r4.Output.Items.Count);
        Assert.Equal(0, r4.Output.Items[0].SourceIndex);
        Assert.Equal(4, r4.Output.Items[4].SourceIndex);
    }

    // Task 6 / 修复 #2：节点上下文中的 position 可能因节点 body 表达式（Jint）写回而变为 double，
    // 读取处须兼容 double，否则会静默归零导致重新从首项迭代。此处模拟 position 已是 double 并跨多次调用验证。
    [Fact]
    public async Task ExecuteAsync_PositionStoredAsDouble_ContinuesIterationAcrossCalls()
    {
        var nodeContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["initialized"] = true,
            ["allItems"] = BuildBatch(5).Items.ToList(),
            ["position"] = 2.0, // double，模拟 JS 写回
            ["processedItems"] = new List<DataItem>()
        };
        var node = new LoopNode { BatchSize = 2 };

        // position=2.0 → 读回兼容 double，跳过前 2 项发 [2,3]（未静默归零重发首项）。
        // 写回时归一为 int（switch 产出 int，position + 窗口大小 = int + int），故此后为 int 4。
        var r1 = await RunAsync(node, CreateStatefulContext(BuildBatch(1), nodeContext), CancellationToken.None);
        Assert.Equal(0, r1.BranchIndex);
        Assert.Equal(2, r1.Output.Items.Count);
        Assert.Equal(2, r1.Output.Items[0].SourceIndex);
        Assert.Equal(3, r1.Output.Items[1].SourceIndex);
        Assert.Equal(4, nodeContext["position"]);

        // position=4 → 发 [4]（double 读回正确，未静默归零）。
        var r2 = await RunAsync(node, CreateStatefulContext(BuildBatch(1), nodeContext), CancellationToken.None);
        Assert.Equal(0, r2.BranchIndex);
        Assert.Single(r2.Output.Items);
        Assert.Equal(4, r2.Output.Items[0].SourceIndex);

        // position=5 >= 5 → Done。
        var r3 = await RunAsync(node, CreateStatefulContext(BuildBatch(1), nodeContext), CancellationToken.None);
        Assert.Equal(1, r3.BranchIndex);
    }

    // Task 6：每次调用都用全新上下文（即非回边激活），循环从头开始，互不串扰。
    [Fact]
    public async Task ExecuteAsync_FreshContextEachCall_AlwaysEmitsFirstWindow()
    {
        var node = new LoopNode { BatchSize = 2 };
        var ct = CancellationToken.None;

        var r1 = await RunAsync(node, CreateStatefulContext(BuildBatch(5), new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)), ct);
        var r2 = await RunAsync(node, CreateStatefulContext(BuildBatch(5), new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)), ct);

        Assert.Equal(0, r1.BranchIndex);
        Assert.Equal(0, r2.BranchIndex);
        Assert.Equal(2, r1.Output.Items.Count);
        Assert.Equal(2, r2.Output.Items.Count);
        Assert.Equal(0, r1.Output.Items[0].SourceIndex);
        Assert.Equal(0, r2.Output.Items[0].SourceIndex);
    }

    // Task 6 / 修复 #5：processedItems 累积的是「下游实际回灌的窗口」，未必等于原始输入全集。
    // 若下游过滤/重排（回灌项少于发出的窗口），Done 输出应为回灌项，而非原始 N 项。
    [Fact]
    public async Task ExecuteAsync_FeedbackReturnsFewerItems_AccumulatesReturnedNotOriginal()
    {
        var nodeContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var node = new LoopNode { BatchSize = 2 };
        var ct = CancellationToken.None;

        // Call 1：5 项原始全集 → 发 [0,1]。
        var r1 = await RunAsync(node, CreateStatefulContext(BuildBatch(5), nodeContext), ct);
        Assert.Equal(0, r1.BranchIndex);
        Assert.Equal(2, r1.Output.Items.Count);

        // 回环输入：下游仅回灌 1 项（过滤掉 idx1），但 Loop 仍按 allItems 发 [2,3]。
        var feedback1 = new DataBatch { Items = { r1.Output.Items[0] } };
        var r2 = await RunAsync(node, CreateStatefulContext(feedback1, nodeContext), ct);
        Assert.Equal(0, r2.BranchIndex);
        Assert.Equal(2, r2.Output.Items.Count);

        // 回环输入：回灌 [2,3]。
        var r3 = await RunAsync(node, CreateStatefulContext(r2.Output, nodeContext), ct);
        Assert.Equal(0, r3.BranchIndex);
        Assert.Single(r3.Output.Items);

        // 回环输入：回灌 [4] → 全部处理完，Done。
        var r4 = await RunAsync(node, CreateStatefulContext(r3.Output, nodeContext), ct);
        Assert.Equal(1, r4.BranchIndex);
        // processedItems = 回灌的 [idx0, idx2, idx3, idx4] 共 4 项，不等于原始 5 项。
        Assert.Equal(4, r4.Output.Items.Count);
        Assert.Equal(0, r4.Output.Items[0].SourceIndex);
        Assert.Equal(4, r4.Output.Items[3].SourceIndex);
    }

    // Task 6：BatchSize<=0 钳制为 1 后，经完整迭代逐批发出并最终 Done（钳制逻辑在迭代路径下也正确）。
    [Fact]
    public async Task ExecuteAsync_BatchSizeZeroClamped_IteratesAsBatchSizeOne()
    {
        var nodeContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var node = new LoopNode { BatchSize = 0 }; // 钳制为 1
        var ct = CancellationToken.None;

        var r1 = await RunAsync(node, CreateStatefulContext(BuildBatch(3), nodeContext), ct);
        Assert.Equal(0, r1.BranchIndex);
        Assert.Single(r1.Output.Items);
        Assert.Equal(0, r1.Output.Items[0].SourceIndex);

        var r2 = await RunAsync(node, CreateStatefulContext(r1.Output, nodeContext), ct);
        Assert.Equal(0, r2.BranchIndex);
        Assert.Single(r2.Output.Items);
        Assert.Equal(1, r2.Output.Items[0].SourceIndex);

        var r3 = await RunAsync(node, CreateStatefulContext(r2.Output, nodeContext), ct);
        Assert.Equal(0, r3.BranchIndex);
        Assert.Single(r3.Output.Items);
        Assert.Equal(2, r3.Output.Items[0].SourceIndex);

        var r4 = await RunAsync(node, CreateStatefulContext(r3.Output, nodeContext), ct);
        Assert.Equal(1, r4.BranchIndex); // Done
        Assert.Equal(3, r4.Output.Items.Count); // 累积 3 项
    }
}
