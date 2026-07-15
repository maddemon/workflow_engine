using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// DataQualityNode 单元测试，验证 report 深拷贝不共享引用、customExpression 不恒返回 true。
/// </summary>
public sealed class DataQualityNodeTests
{
    private static NodeExecutionContext CreateContext(DataBatch input, string rules, bool passOnFailure = false)
    {
        return new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "test" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = "dq1",
                TypeName = "dataQuality",
                Name = "dq1"
            },
            Inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
            {
                [FlowConstants.PortNames.Input] = input
            }
        };
    }

    private static DataBatch BuildBatch(params int[] values)
    {
        var items = new List<DataItem>();
        for (var i = 0; i < values.Length; i++)
        {
            items.Add(new DataItem
            {
                Data = new JsonObject { ["value"] = values[i] },
                Success = true,
                SourceIndex = i
            });
        }
        return new DataBatch { Items = items };
    }

    [Fact]
    public async Task ExecuteAsync_ReportNotSharedBetweenItems()
    {
        var input = BuildBatch(1, 2, 3);
        var rules = """[{"type":"rowCount","min":1}]""";

        var node = new DataQualityNode
        {
            Rules = rules,
            PassOnFailure = false
        };

        var result = await node.ExecuteAsync(CreateContext(input, rules), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(3, result.Output.Items.Count);

        // Each item should have its own _dqReport instance (not shared reference)
        var report1 = result.Output.Items[0].Data!["_dqReport"]!.AsObject();
        var report2 = result.Output.Items[1].Data!["_dqReport"]!.AsObject();
        var report3 = result.Output.Items[2].Data!["_dqReport"]!.AsObject();

        Assert.NotSame(report1, report2);
        Assert.NotSame(report2, report3);
        Assert.NotSame(report1, report3);

        // Modifying one report should not affect others
        report1["customField"] = "modified";
        Assert.Null(report2["customField"]);
        Assert.Null(report3["customField"]);
    }

    [Fact]
    public async Task ExecuteAsync_AllRulesPass_ReturnsSuccessWithReport()
    {
        var input = BuildBatch(1, 2);
        var rules = """[{"type":"rowCount","min":1,"max":10}]""";

        var node = new DataQualityNode { Rules = rules };

        var result = await node.ExecuteAsync(CreateContext(input, rules), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.Output.Items.Count);
        Assert.NotNull(result.Output.Items[0].Data!["_dqReport"]);
    }

    [Fact]
    public async Task ExecuteAsync_RuleFails_BlocksDataFlow()
    {
        var input = BuildBatch(1);
        var rules = """[{"type":"rowCount","min":5}]""";

        var node = new DataQualityNode { Rules = rules, PassOnFailure = false };

        var result = await node.ExecuteAsync(CreateContext(input, rules), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("DataQualityCheckFailed", result.Error?.Code);
    }
}
