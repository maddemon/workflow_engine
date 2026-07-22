using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// ListOpsNode（N05）测试：覆盖 4 种运算、空输入、非数值与缺参错误路径。
/// 沿用 DateTimeNodeTests / CryptoNodeTests 的模式：用占位节点构建上下文，再用独立配置的节点执行，
/// 避免参数水合覆盖手动设置的节点属性。
/// </summary>
public sealed class ListOpsNodeTests
{
    private static DataBatch Items(params JsonObject[] items) => new() { Items = [.. items.Select((o, i) => new DataItem { Data = o, Success = true, SourceIndex = i })] };

    /// <summary>
    /// 类型容忍地读取数值节点：优先 decimal 直读，失败则回退到 JSON 文本解析，
    /// 以规避 System.Text.Json 对 JsonValue 底层 CLR 类型的严格 GetValue&lt;T&gt; 匹配。
    /// </summary>
    private static decimal Num(JsonNode? node) =>
        node is JsonValue jv && jv.TryGetValue<decimal>(out var d)
            ? d
            : decimal.Parse(node!.ToString()!, CultureInfo.InvariantCulture);

    /// <summary>
    /// 用占位节点构建上下文（水合只作用于占位节点），再用 <paramref name="node"/> 执行，保留其手动设置的属性。
    /// </summary>
    private static async Task<NodeExecutionResult> RunAsync(ListOpsNode node, DataBatch input)
    {
        var context = await NodeTestContextFactory.BuildAsync(
            new ListOpsNode(),
            new Dictionary<string, object>(),
            new() { [FlowConstants.PortNames.Input] = input });
        return await node.ExecuteAsync(context, CancellationToken.None);
    }

    // ---- summarize: sum ----
    [Fact]
    public async Task Summarize_Sum_AggregatesField()
    {
        var node = new ListOpsNode { Operation = ListOpsOperation.Summarize, Field = "amount", Aggregate = ListOpsAggregate.Sum };
        var result = await RunAsync(node, Items(
            JsonNode.Parse("""{"amount":10}""")!.AsObject(),
            JsonNode.Parse("""{"amount":20}""")!.AsObject(),
            JsonNode.Parse("""{"amount":30}""")!.AsObject()));

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal("amount", data["field"]?.GetValue<string>());
        Assert.Equal(60m, Num(data["value"]));
        Assert.Equal(3m, Num(data["count"]));
    }

    // ---- summarize: avg (non-integral) ----
    [Fact]
    public async Task Summarize_Avg_AggregatesField()
    {
        var node = new ListOpsNode { Operation = ListOpsOperation.Summarize, Field = "amount", Aggregate = ListOpsAggregate.Avg };
        var result = await RunAsync(node, Items(
            JsonNode.Parse("""{"amount":10}""")!.AsObject(),
            JsonNode.Parse("""{"amount":25}""")!.AsObject()));

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal(17.5m, Num(data["value"]));
        Assert.Equal(2m, Num(data["count"]));
    }

    // ---- summarize: min ----
    [Fact]
    public async Task Summarize_Min_ReturnsMinimum()
    {
        var node = new ListOpsNode { Operation = ListOpsOperation.Summarize, Field = "amount", Aggregate = ListOpsAggregate.Min };
        var result = await RunAsync(node, Items(
            JsonNode.Parse("""{"amount":10}""")!.AsObject(),
            JsonNode.Parse("""{"amount":20}""")!.AsObject(),
            JsonNode.Parse("""{"amount":30}""")!.AsObject()));

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal(10m, Num(data["value"]));
    }

    // ---- summarize: max ----
    [Fact]
    public async Task Summarize_Max_ReturnsMaximum()
    {
        var node = new ListOpsNode { Operation = ListOpsOperation.Summarize, Field = "amount", Aggregate = ListOpsAggregate.Max };
        var result = await RunAsync(node, Items(
            JsonNode.Parse("""{"amount":10}""")!.AsObject(),
            JsonNode.Parse("""{"amount":20}""")!.AsObject(),
            JsonNode.Parse("""{"amount":30}""")!.AsObject()));

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal(30m, Num(data["value"]));
    }

    // ---- summarize: count (ignores numeric) ----
    [Fact]
    public async Task Summarize_Count_CountsItems()
    {
        var node = new ListOpsNode { Operation = ListOpsOperation.Summarize, Field = "amount", Aggregate = ListOpsAggregate.Count };
        var result = await RunAsync(node, Items(
            JsonNode.Parse("""{"amount":10}""")!.AsObject(),
            JsonNode.Parse("""{"amount":"abc"}""")!.AsObject()));

        Assert.True(result.Success);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal(2m, Num(data["value"]));
        Assert.Equal(2m, Num(data["count"]));
    }

    // ---- fieldToItems ----
    [Fact]
    public async Task FieldToItems_SplitsArrayIntoItems()
    {
        var node = new ListOpsNode { Operation = ListOpsOperation.FieldToItems, Field = "tags" };
        var result = await RunAsync(node, Items(
            JsonNode.Parse("""{"tags":["a","b","c"]}""")!.AsObject(),
            JsonNode.Parse("""{"tags":["x"]}""")!.AsObject()));

        Assert.True(result.Success);
        Assert.Equal(4, result.Output.Items.Count);
        Assert.Equal("a", result.Output.Items[0].Data!["value"]?.GetValue<string>());
        Assert.Equal("b", result.Output.Items[1].Data!["value"]?.GetValue<string>());
        Assert.Equal("c", result.Output.Items[2].Data!["value"]?.GetValue<string>());
        Assert.Equal("x", result.Output.Items[3].Data!["value"]?.GetValue<string>());
    }

    // ---- itemsToField ----
    [Fact]
    public async Task ItemsToField_CollectsIntoArray()
    {
        var node = new ListOpsNode { Operation = ListOpsOperation.ItemsToField, Field = "name" };
        var result = await RunAsync(node, Items(
            JsonNode.Parse("""{"name":"a"}""")!.AsObject(),
            JsonNode.Parse("""{"name":"b"}""")!.AsObject()));

        Assert.True(result.Success);
        Assert.Single(result.Output.Items);
        var data = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        var arr = Assert.IsType<JsonArray>(data["name"]);
        Assert.Equal(2, arr.Count);
        Assert.Equal("a", arr[0]?.GetValue<string>());
        Assert.Equal("b", arr[1]?.GetValue<string>());
    }

    // ---- groupBy ----
    [Fact]
    public async Task GroupBy_Sum_PerGroup()
    {
        var node = new ListOpsNode
        {
            Operation = ListOpsOperation.GroupBy,
            GroupBy = "cat",
            Field = "amount",
            Aggregate = ListOpsAggregate.Sum
        };
        var result = await RunAsync(node, Items(
            JsonNode.Parse("""{"cat":"x","amount":10}""")!.AsObject(),
            JsonNode.Parse("""{"cat":"x","amount":20}""")!.AsObject(),
            JsonNode.Parse("""{"cat":"y","amount":5}""")!.AsObject()));

        Assert.True(result.Success);
        Assert.Equal(2, result.Output.Items.Count);
        var gx = Assert.IsType<JsonObject>(result.Output.Items[0].Data);
        Assert.Equal("x", gx["group"]?.GetValue<string>());
        Assert.Equal(30m, Num(gx["value"]));
        var gy = Assert.IsType<JsonObject>(result.Output.Items[1].Data);
        Assert.Equal("y", gy["group"]?.GetValue<string>());
        Assert.Equal(5m, Num(gy["value"]));
    }

    // ---- empty input -> empty batch ----
    [Fact]
    public async Task EmptyInput_ReturnsEmptyBatch()
    {
        var node = new ListOpsNode { Operation = ListOpsOperation.Summarize, Field = "amount", Aggregate = ListOpsAggregate.Sum };
        var result = await RunAsync(node, new DataBatch());

        Assert.True(result.Success);
        Assert.Empty(result.Output.Items);
    }

    // ---- non-numeric for sum -> ErrorResult ----
    [Fact]
    public async Task Summarize_NonNumeric_Sum_ReturnsError()
    {
        var node = new ListOpsNode { Operation = ListOpsOperation.Summarize, Field = "amount", Aggregate = ListOpsAggregate.Sum };
        var result = await RunAsync(node, Items(JsonNode.Parse("""{"amount":"abc"}""")!.AsObject()));

        Assert.False(result.Success);
        Assert.Equal("InvalidAggregateValue", result.Error?.Code);
    }

    // ---- non-numeric for min -> ErrorResult ----
    [Fact]
    public async Task Summarize_NonNumeric_Min_ReturnsError()
    {
        var node = new ListOpsNode { Operation = ListOpsOperation.Summarize, Field = "amount", Aggregate = ListOpsAggregate.Min };
        var result = await RunAsync(node, Items(JsonNode.Parse("""{"amount":"oops"}""")!.AsObject()));

        Assert.False(result.Success);
        Assert.Equal("InvalidAggregateValue", result.Error?.Code);
    }

    // ---- missing Field for summarize -> ErrorResult ----
    [Fact]
    public async Task Summarize_MissingField_ReturnsError()
    {
        var node = new ListOpsNode { Operation = ListOpsOperation.Summarize, Aggregate = ListOpsAggregate.Sum };
        var result = await RunAsync(node, Items(JsonNode.Parse("""{"amount":1}""")!.AsObject()));

        Assert.False(result.Success);
        Assert.Equal("MissingField", result.Error?.Code);
    }

    // ---- missing GroupBy for groupBy -> ErrorResult ----
    [Fact]
    public async Task GroupBy_MissingGroupBy_ReturnsError()
    {
        var node = new ListOpsNode { Operation = ListOpsOperation.GroupBy, Field = "amount", Aggregate = ListOpsAggregate.Sum };
        var result = await RunAsync(node, Items(JsonNode.Parse("""{"amount":1}""")!.AsObject()));

        Assert.False(result.Success);
        Assert.Equal("MissingGroupBy", result.Error?.Code);
    }

    // ---- fieldToItems: field not an array -> ErrorResult ----
    [Fact]
    public async Task FieldToItems_FieldNotArray_ReturnsError()
    {
        var node = new ListOpsNode { Operation = ListOpsOperation.FieldToItems, Field = "tags" };
        var result = await RunAsync(node, Items(JsonNode.Parse("""{"tags":"notarray"}""")!.AsObject()));

        Assert.False(result.Success);
        Assert.Equal("FieldNotArray", result.Error?.Code);
    }

    // ---- unknown operation -> ErrorResult ----
    [Fact]
    public async Task UnknownOperation_ReturnsError()
    {
        var node = new ListOpsNode { Operation = (ListOpsOperation)999 };
        var result = await RunAsync(node, Items(JsonNode.Parse("""{"amount":1}""")!.AsObject()));

        Assert.False(result.Success);
        Assert.Equal("UnknownOperation", result.Error?.Code);
    }
}
