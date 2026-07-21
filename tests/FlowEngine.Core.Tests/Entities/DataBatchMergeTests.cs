using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Tests.Entities;

public sealed class DataBatchMergeTests
{
    [Fact]
    public void Merge_CombinesItems_AndReindexesSourceIndex()
    {
        var a = new DataBatch
        {
            Items =
            [
                new DataItem { Data = JsonValue.Create(1), Success = true, SourceIndex = 0 },
                new DataItem { Data = JsonValue.Create(2), Success = true, SourceIndex = 1 }
            ]
        };
        var b = new DataBatch
        {
            Items =
            [
                new DataItem { Data = JsonValue.Create(3), Success = true, SourceIndex = 0 },
                new DataItem { Data = JsonValue.Create(4), Success = true, SourceIndex = 1 }
            ]
        };

        var merged = DataBatch.Merge(a, b);

        Assert.Equal(4, merged.Items.Count);
        Assert.Equal(0, merged.Items[0].SourceIndex);
        Assert.Equal(1, merged.Items[1].SourceIndex);
        Assert.Equal(2, merged.Items[2].SourceIndex);
        Assert.Equal(3, merged.Items[3].SourceIndex);
        Assert.Equal(1, merged.Items[0].Data?.GetValue<int>());
        Assert.Equal(4, merged.Items[3].Data?.GetValue<int>());
        // 不修改入参
        Assert.Equal(2, a.Items.Count);
        Assert.Equal(2, b.Items.Count);
    }

    [Fact]
    public void Merge_WithEmpty_KeepsOtherItems_AndReindexesFromZero()
    {
        var a = new DataBatch { Items = [new DataItem { Data = JsonValue.Create(1), Success = true, SourceIndex = 0 }] };

        var mergedWithEmpty = DataBatch.Merge(a, new DataBatch());
        Assert.Single(mergedWithEmpty.Items);
        Assert.Equal(0, mergedWithEmpty.Items[0].SourceIndex);

        var emptyWithA = DataBatch.Merge(new DataBatch(), a);
        Assert.Single(emptyWithA.Items);
        Assert.Equal(0, emptyWithA.Items[0].SourceIndex);
    }
}
