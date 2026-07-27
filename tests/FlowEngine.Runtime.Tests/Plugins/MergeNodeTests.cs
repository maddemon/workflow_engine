using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// MergeNode 单元测试，重点验证 CombineByField 模式下重复键不崩溃。
/// </summary>
public sealed class MergeNodeTests
{
    private static NodeExecutionContext CreateContext(DataBatch batch1, DataBatch batch2)
    {
        return new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "test" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = "merge1",
                TypeName = "merge",
                Name = "merge1"
            },
            Inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
            {
                [FlowConstants.PortNames.Input1] = batch1,
                [FlowConstants.PortNames.Input2] = batch2
            }
        };
    }

    private static DataBatch BuildBatch(params (string id, string name)[] rows)
    {
        var items = new List<DataItem>();
        for (var i = 0; i < rows.Length; i++)
        {
            items.Add(new DataItem
            {
                Data = new JsonObject { ["id"] = rows[i].id, ["name"] = rows[i].name },
                Success = true,
                SourceIndex = i
            });
        }
        return new DataBatch { Items = items };
    }

    [Fact]
    public async Task ExecuteAsync_CombineByField_DuplicateKeys_DoesNotCrash()
    {
        var batch1 = BuildBatch(("1", "Alice"));
        var batch2 = BuildBatch(("1", "Alpha"), ("1", "Beta"));

        var node = new MergeNode
        {
            Mode = MergeMode.Combine,
            CombineOperation = CombineOperation.CombineByField,
            MatchField = "id"
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(batch1, batch2), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
        var merged = result.Output.Items[0].Data!.AsObject();
        Assert.Equal("1", merged["id"]!.GetValue<string>());
        // Should merge with the first matching item from batch2
        Assert.Equal("Alpha", merged["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_CombineByField_NoMatch_PassesItemThrough()
    {
        var batch1 = BuildBatch(("1", "Alice"), ("2", "Bob"));
        var batch2 = BuildBatch(("2", "Beta"));

        var node = new MergeNode
        {
            Mode = MergeMode.Combine,
            CombineOperation = CombineOperation.CombineByField,
            MatchField = "id"
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(batch1, batch2), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
        // First item (id=1) has no match, passes through with original name
        Assert.Equal("Alice", result.Output.Items[0].Data!["name"]!.GetValue<string>());
        // Second item (id=2) merged with batch2
        Assert.Equal("Beta", result.Output.Items[1].Data!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_CombineByField_NormalMerge()
    {
        var batch1 = BuildBatch(("1", "Alice"));
        var batch2 = BuildBatch(("1", "Updated"));

        var node = new MergeNode
        {
            Mode = MergeMode.Combine,
            CombineOperation = CombineOperation.CombineByField,
            MatchField = "id"
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(batch1, batch2), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
        Assert.Equal("Updated", result.Output.Items[0].Data!["name"]!.GetValue<string>());
    }

    private static DataBatch BuildBatchWithStatus(bool success, string message, params (string id, string name)[] rows)
    {
        var items = new List<DataItem>();
        for (var i = 0; i < rows.Length; i++)
        {
            items.Add(new DataItem
            {
                Data = new JsonObject { ["id"] = rows[i].id, ["name"] = rows[i].name },
                Success = success,
                Error = success ? null : new NodeError { Code = "UpstreamError", Message = message },
                SourceIndex = i
            });
        }
        return new DataBatch { Items = items };
    }

    [Fact]
    public async Task ExecuteAsync_CombineByPosition_FailedSourceItem_PropagatesSuccessAndError()
    {
        var failedItem = new DataItem
        {
            Data = new JsonObject { ["id"] = "1", ["name"] = "Alice" },
            Success = false,
            Error = new NodeError { Code = "UpstreamError", Message = "上游失败" },
            SourceIndex = 0
        };
        var succeededItem = new DataItem
        {
            Data = new JsonObject { ["id"] = "2", ["name"] = "Bob" },
            Success = true,
            SourceIndex = 1
        };
        var batch1 = new DataBatch { Items = new List<DataItem> { failedItem, succeededItem } };
        var batch2 = BuildBatch(("1", "Alpha"), ("2", "Beta"));

        var node = new MergeNode
        {
            Mode = MergeMode.Combine,
            CombineOperation = CombineOperation.CombineByPosition
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(batch1, batch2), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
        // First merged item derives from a failed source -> must remain failed with error
        Assert.False(result.Output.Items[0].Success);
        Assert.NotNull(result.Output.Items[0].Error);
        Assert.Equal("上游失败", result.Output.Items[0].Error!.Message);
        // Second merged item derives from a successful source -> remains successful
        Assert.True(result.Output.Items[1].Success);
    }

    [Fact]
    public async Task ExecuteAsync_CombineByField_FailedSourceItem_PropagatesSuccessAndError()
    {
        var batch1 = BuildBatchWithStatus(false, "上游失败", ("1", "Alice"));
        var batch2 = BuildBatch(("1", "Updated"));

        var node = new MergeNode
        {
            Mode = MergeMode.Combine,
            CombineOperation = CombineOperation.CombineByField,
            MatchField = "id"
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(batch1, batch2), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
        Assert.False(result.Output.Items[0].Success);
        Assert.NotNull(result.Output.Items[0].Error);
        Assert.Equal("上游失败", result.Output.Items[0].Error!.Message);
    }

    [Fact]
    public async Task ExecuteAsync_Multiplex_FailedSourceItem_PropagatesSuccessAndError()
    {
        var batch1 = BuildBatchWithStatus(false, "上游失败", ("1", "Alice"));
        var batch2 = BuildBatch(("2", "Beta"), ("3", "Gamma"));

        var node = new MergeNode { Mode = MergeMode.Multiplex };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(batch1, batch2), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
        // Every multiplexed output derives from the failed source -> all must remain failed
        foreach (var item in result.Output.Items)
        {
            Assert.False(item.Success);
            Assert.NotNull(item.Error);
            Assert.Equal("上游失败", item.Error!.Message);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Append_CombinesAllItems()
    {
        var batch1 = BuildBatch(("1", "Alice"));
        var batch2 = BuildBatch(("2", "Bob"));

        var node = new MergeNode
        {
            Mode = MergeMode.Append
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(batch1, batch2), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.Output.Items.Count);
    }
}
