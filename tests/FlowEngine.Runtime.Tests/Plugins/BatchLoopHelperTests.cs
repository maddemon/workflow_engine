using System.Collections.Generic;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// BatchLoopHelper 单元测试：覆盖首批窗口、末批超调、BatchSize=1、空输入、
/// position int/double 兼容、Done 触发。供 LoopNode 与未来 batchSplit 节点复用验证。
/// </summary>
public sealed class BatchLoopHelperTests
{
    private static List<DataItem> BuildItems(int count)
    {
        var items = new List<DataItem>();
        for (var i = 0; i < count; i++)
        {
            items.Add(new DataItem { SourceIndex = i });
        }

        return items;
    }

    private static IDictionary<string, object?> NewContext() =>
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void EnsureInitialized_FirstCall_CachesAllItemsAndResetsPosition()
    {
        var ctx = NewContext();
        var all = BuildItems(5);

        var first = BatchLoopHelper.EnsureInitialized(ctx, all);

        Assert.True(first);
        Assert.True(ctx.ContainsKey(BatchLoopHelper.KeyInitialized));
        Assert.Equal(5, ctx.Get<List<DataItem>>(BatchLoopHelper.KeyAllItems)!.Count);
        Assert.Equal(0, BatchLoopHelper.ReadPosition(ctx));
    }

    [Fact]
    public void EnsureInitialized_SecondCall_NoReinitAndReturnsFalse()
    {
        var ctx = NewContext();
        BatchLoopHelper.EnsureInitialized(ctx, BuildItems(5));
        var wasFirst = BatchLoopHelper.EnsureInitialized(ctx, BuildItems(9));

        Assert.False(wasFirst);
        Assert.Equal(5, ctx.Get<List<DataItem>>(BatchLoopHelper.KeyAllItems)!.Count);
    }

    [Fact]
    public void EmitNextWindow_FirstWindow_ReturnsLoopWithFirstBatchSizeItems()
    {
        var ctx = NewContext();
        BatchLoopHelper.EnsureInitialized(ctx, BuildItems(5));
        var done = new DataBatch { Items = [] };

        var r = BatchLoopHelper.EmitNextWindow(ctx, 2, done);

        Assert.Equal(BatchLoopHelper.BranchLoop, r.BranchIndex);
        Assert.Equal(2, r.Output.Items.Count);
        Assert.Equal(0, r.Output.Items[0].SourceIndex);
        Assert.Equal(1, r.Output.Items[1].SourceIndex);
    }

    [Fact]
    public void EmitNextWindow_BatchSizeOne_EmitsSingleItemPerCall()
    {
        var ctx = NewContext();
        BatchLoopHelper.EnsureInitialized(ctx, BuildItems(3));
        var done = new DataBatch { Items = [] };

        var r1 = BatchLoopHelper.EmitNextWindow(ctx, 1, done);
        Assert.Equal(BatchLoopHelper.BranchLoop, r1.BranchIndex);
        Assert.Single(r1.Output.Items);
        Assert.Equal(0, r1.Output.Items[0].SourceIndex);

        var r2 = BatchLoopHelper.EmitNextWindow(ctx, 1, done);
        Assert.Equal(BatchLoopHelper.BranchLoop, r2.BranchIndex);
        Assert.Single(r2.Output.Items);
        Assert.Equal(1, r2.Output.Items[0].SourceIndex);
    }

    [Fact]
    public void EmitNextWindow_LastBatch_UsesActualWindowSize_NoOvershoot()
    {
        // 5 项，BatchSize=2：批次为 [0,1]→[2,3]→[4]。末批 1 项，position 推进到 5（=count），不超调到 6。
        var ctx = NewContext();
        BatchLoopHelper.EnsureInitialized(ctx, BuildItems(5));
        var done = new DataBatch { Items = [] };

        BatchLoopHelper.EmitNextWindow(ctx, 2, done); // pos 0→2
        BatchLoopHelper.EmitNextWindow(ctx, 2, done); // pos 2→4
        var last = BatchLoopHelper.EmitNextWindow(ctx, 2, done); // pos 4→5，末批 1 项

        Assert.Equal(BatchLoopHelper.BranchLoop, last.BranchIndex);
        Assert.Single(last.Output.Items);
        Assert.Equal(4, last.Output.Items[0].SourceIndex);
        Assert.Equal(5, BatchLoopHelper.ReadPosition(ctx)); // 恰好 = count，无超调
    }

    [Fact]
    public void EmitNextWindow_EmptyInput_ReturnsDoneWithEmptyPayload()
    {
        var ctx = NewContext();
        BatchLoopHelper.EnsureInitialized(ctx, []);
        var done = new DataBatch { Items = [] };

        var r = BatchLoopHelper.EmitNextWindow(ctx, 2, done);

        Assert.Equal(BatchLoopHelper.BranchDone, r.BranchIndex);
        Assert.Empty(r.Output.Items);
    }

    [Fact]
    public void ReadPosition_DoubleValue_ReadAsIntWithoutSilentZero()
    {
        var ctx = NewContext();
        ctx[BatchLoopHelper.KeyPosition] = 2.0;
        Assert.Equal(2, BatchLoopHelper.ReadPosition(ctx));
    }

    [Fact]
    public void ReadPosition_IntValue_ReadDirectly()
    {
        var ctx = NewContext();
        ctx[BatchLoopHelper.KeyPosition] = 3;
        Assert.Equal(3, BatchLoopHelper.ReadPosition(ctx));
    }

    [Fact]
    public void ReadPosition_MissingOrUnknown_ReturnsZero()
    {
        Assert.Equal(0, BatchLoopHelper.ReadPosition(NewContext()));

        var ctx = NewContext();
        ctx[BatchLoopHelper.KeyPosition] = "not-a-number";
        Assert.Equal(0, BatchLoopHelper.ReadPosition(ctx));
    }

    [Fact]
    public void EmitNextWindow_PositionStoredAsDouble_ContinuesIteration()
    {
        // 模拟 Jint 写回 position 为 double：ReadPosition 兼容，迭代不静默归零。
        var ctx = NewContext();
        BatchLoopHelper.EnsureInitialized(ctx, BuildItems(5));
        ctx[BatchLoopHelper.KeyPosition] = 2.0;
        var done = new DataBatch { Items = [] };

        var r = BatchLoopHelper.EmitNextWindow(ctx, 2, done);
        Assert.Equal(BatchLoopHelper.BranchLoop, r.BranchIndex);
        Assert.Equal(2, r.Output.Items.Count);
        Assert.Equal(2, r.Output.Items[0].SourceIndex);
        Assert.Equal(3, r.Output.Items[1].SourceIndex);
        Assert.Equal(4, BatchLoopHelper.ReadPosition(ctx)); // 写回归一为 int
    }

    [Fact]
    public void EmitNextWindow_PositionAtEnd_TriggersDoneWithPayload()
    {
        var ctx = NewContext();
        BatchLoopHelper.EnsureInitialized(ctx, BuildItems(5));
        ctx[BatchLoopHelper.KeyPosition] = 5; // 已达末尾
        var doneItems = BuildItems(5);
        var done = new DataBatch { Items = doneItems };

        var r = BatchLoopHelper.EmitNextWindow(ctx, 2, done);

        Assert.Equal(BatchLoopHelper.BranchDone, r.BranchIndex);
        Assert.Equal(5, r.Output.Items.Count);
        Assert.Same(doneItems, r.Output.Items);
    }
}
