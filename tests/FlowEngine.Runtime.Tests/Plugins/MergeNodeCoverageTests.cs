using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// MergeNode 补充覆盖测试，验证 CombineByPosition / MergeByPosition / Multiplex 与空字段回退。
/// </summary>
public sealed class MergeNodeCoverageTests
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

    private static DataBatch BuildBatch(params JsonObject[] rows)
    {
        var items = rows.Select((r, i) => new DataItem { Data = r, Success = true, SourceIndex = i }).ToList();
        return new DataBatch { Items = items };
    }

    [Fact]
    public async Task ExecuteAsync_CombineByPosition_MergesByIndex()
    {
        var batch1 = BuildBatch(new JsonObject { ["id"] = 1, ["a"] = "A" });
        var batch2 = BuildBatch(new JsonObject { ["b"] = "B" });

        var node = new MergeNode
        {
            Mode = MergeMode.Combine,
            CombineOperation = CombineOperation.CombineByPosition
        };

        var result = await node.ExecuteAsync(CreateContext(batch1, batch2), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
        var merged = result.Output.Items[0].Data as JsonObject;
        Assert.Equal(1, merged!["id"]!.GetValue<int>());
        Assert.Equal("A", merged["a"]!.GetValue<string>());
        Assert.Equal("B", merged["b"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_CombineByPosition_DifferentLengths_PadsWithNull()
    {
        var batch1 = BuildBatch(new JsonObject { ["a"] = "A" });
        var batch2 = BuildBatch();

        var node = new MergeNode
        {
            Mode = MergeMode.Combine,
            CombineOperation = CombineOperation.CombineByPosition
        };

        var result = await node.ExecuteAsync(CreateContext(batch1, batch2), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
        var data = result.Output.Items[0].Data as JsonObject;
        Assert.NotNull(data);
        Assert.Equal("A", data["a"]!.GetValue<string>());
        Assert.Null(data["b"]);
    }

    [Fact]
    public async Task ExecuteAsync_CombineByFieldEmptyField_Appends()
    {
        var batch1 = BuildBatch(new JsonObject { ["id"] = 1 });
        var batch2 = BuildBatch(new JsonObject { ["id"] = 2 });

        var node = new MergeNode
        {
            Mode = MergeMode.Combine,
            CombineOperation = CombineOperation.CombineByField,
            MatchField = ""
        };

        var result = await node.ExecuteAsync(CreateContext(batch1, batch2), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
    }

    [Fact]
    public async Task ExecuteAsync_MergeByPosition_PrefersFirstInput()
    {
        var batch1 = BuildBatch(new JsonObject { ["id"] = 1, ["name"] = "First" });
        var batch2 = BuildBatch(new JsonObject { ["name"] = "Second" });

        var node = new MergeNode
        {
            Mode = MergeMode.Combine,
            CombineOperation = CombineOperation.MergeByPosition
        };

        var result = await node.ExecuteAsync(CreateContext(batch1, batch2), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
        Assert.Equal("First", result.Output.Items[0].Data!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_Multiplex_CrossCombines()
    {
        var batch1 = BuildBatch(new JsonObject { ["a"] = 1 }, new JsonObject { ["a"] = 2 });
        var batch2 = BuildBatch(new JsonObject { ["b"] = 3 }, new JsonObject { ["b"] = 4 });

        var node = new MergeNode { Mode = MergeMode.Multiplex };

        var result = await node.ExecuteAsync(CreateContext(batch1, batch2), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(4, result.Output.Items.Count);
    }
}
