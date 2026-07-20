using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// DataQualityNode 补充覆盖测试，聚焦 fieldNotNull / fieldPattern / fieldRange / PassOnFailure 等路径。
/// </summary>
public sealed class DataQualityNodeCoverageTests
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
            },
            ResolvedParameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["rules"] = rules,
                ["passOnFailure"] = passOnFailure
            }
        };
    }

    private static DataBatch BuildBatch(params (string name, string? value)[] rows)
    {
        var items = new List<DataItem>();
        for (var i = 0; i < rows.Length; i++)
        {
            items.Add(new DataItem
            {
                Data = rows[i].value is null ? null : new JsonObject { [rows[i].name] = rows[i].value },
                Success = true,
                SourceIndex = i
            });
        }

        return new DataBatch { Items = items };
    }

    [Fact]
    public async Task ExecuteAsync_InvalidRules_ReturnsError()
    {
        var input = BuildBatch(("email", "a@b.com"));
        var node = new DataQualityNode { Rules = "not-json" };

        var result = await node.ExecuteAsync(CreateContext(input, "not-json"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("InvalidRules", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyRules_PassesThrough()
    {
        var input = BuildBatch(("email", "a@b.com"));
        var node = new DataQualityNode { Rules = "[]" };

        var result = await node.ExecuteAsync(CreateContext(input, "[]"), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
    }

    [Fact]
    public async Task ExecuteAsync_FieldNotNull_FailWhenMissing()
    {
        var input = BuildBatch(("email", "a@b.com"), ("email", null));
        var rules = """[{"type":"fieldNotNull","field":"email"}]""";
        var node = new DataQualityNode { Rules = rules, PassOnFailure = false };

        var result = await node.ExecuteAsync(CreateContext(input, rules), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("DataQualityCheckFailed", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_FieldPattern_PassWhenMatches()
    {
        var input = BuildBatch(("email", "a@b.com"), ("email", "c@d.com"));
        var rules = """[{"type":"fieldPattern","field":"email","pattern":"^[^@]+@[^@]+$"}]""";
        var node = new DataQualityNode { Rules = rules };

        var result = await node.ExecuteAsync(CreateContext(input, rules), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
    }

    [Fact]
    public async Task ExecuteAsync_FieldPattern_FailWhenMismatch()
    {
        var input = BuildBatch(("email", "not-an-email"));
        var rules = """[{"type":"fieldPattern","field":"email","pattern":"^[^@]+@[^@]+$"}]""";
        var node = new DataQualityNode { Rules = rules };

        var result = await node.ExecuteAsync(CreateContext(input, rules), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("DataQualityCheckFailed", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_FieldPattern_InvalidRegex_ReturnsError()
    {
        var input = BuildBatch(("email", "a@b.com"));
        var rules = """[{"type":"fieldPattern","field":"email","pattern":"["}]""";
        var node = new DataQualityNode { Rules = rules };

        var result = await node.ExecuteAsync(CreateContext(input, rules), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("DataQualityCheckFailed", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_FieldRange_PassWhenInRange()
    {
        var input = BuildBatch(("age", "18"), ("age", "65"));
        var rules = """[{"type":"fieldRange","field":"age","min":0,"max":120}]""";
        var node = new DataQualityNode { Rules = rules };

        var result = await node.ExecuteAsync(CreateContext(input, rules), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
    }

    [Fact]
    public async Task ExecuteAsync_FieldRange_FailWhenOutOfRange()
    {
        var input = BuildBatch(("age", "200"));
        var rules = """[{"type":"fieldRange","field":"age","min":0,"max":120}]""";
        var node = new DataQualityNode { Rules = rules };

        var result = await node.ExecuteAsync(CreateContext(input, rules), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("DataQualityCheckFailed", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_FieldRange_NonNumeric_Fails()
    {
        var input = BuildBatch(("age", "old"));
        var rules = """[{"type":"fieldRange","field":"age","min":0,"max":120}]""";
        var node = new DataQualityNode { Rules = rules };

        var result = await node.ExecuteAsync(CreateContext(input, rules), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("DataQualityCheckFailed", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_PassOnFailureTrue_PassesDataThrough()
    {
        var input = BuildBatch(("email", "a@b.com"));
        var rules = """[{"type":"rowCount","min":5}]""";
        var node = new DataQualityNode { Rules = rules, PassOnFailure = true };

        var result = await node.ExecuteAsync(CreateContext(input, rules, true), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Single(result.Output.Items);
        Assert.NotNull(result.Output.Items[0].Data!["_dqReport"]);
    }

    [Fact]
    public async Task ExecuteAsync_NonObjectInput_WrapsOriginal()
    {
        var input = new DataBatch
        {
            Items =
            [
                new DataItem { Data = JsonValue.Create("raw"), Success = true, SourceIndex = 0 }
            ]
        };
        var rules = """[{"type":"rowCount","min":1,"max":10}]""";
        var node = new DataQualityNode { Rules = rules };

        var result = await node.ExecuteAsync(CreateContext(input, rules), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("raw", result.Output.Items[0].Data!["_original"]!.GetValue<string>());
    }
}
