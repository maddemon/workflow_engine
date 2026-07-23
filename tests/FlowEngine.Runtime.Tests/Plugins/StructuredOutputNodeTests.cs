using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Scripting;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// structuredOutput 节点测试：覆盖合法 JSON 对象、Schema 必填校验、Strict 开关、非法/非对象输入、空输入、表达式求值。
/// </summary>
public sealed class StructuredOutputNodeTests
{
    [Fact]
    public async Task ExecuteAsync_ValidJsonObject_NoSchema_ReturnsSameFields()
    {
        var node = new StructuredOutputNode();
        var context = await BuildContextAsync(node, new() { ["input"] = Literal("{\"name\":\"Alice\",\"age\":30}") }, new DataBatch());

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
        var data = result.Output.Items[0].Data as JsonObject;
        Assert.NotNull(data);
        Assert.Equal("Alice", data!["name"]?.GetValue<string>());
        Assert.Equal(30, data["age"]?.GetValue<int>());
    }

    [Fact]
    public async Task ExecuteAsync_SchemaRequiredKeysPresent_Passes()
    {
        var node = new StructuredOutputNode();
        var context = await BuildContextAsync(node, new()
        {
            ["input"] = Literal("{\"name\":\"Alice\",\"age\":30}"),
            ["schema"] = Literal("{\"required\":[\"name\",\"age\"]}")
        }, new DataBatch());

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
        Assert.Equal("Alice", result.Output.Items[0].Data?["name"]?.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_MissingRequiredKey_StrictTrue_ReturnsSchemaValidationFailed()
    {
        var node = new StructuredOutputNode();
        var context = await BuildContextAsync(node, new()
        {
            ["input"] = Literal("{\"name\":\"Alice\"}"),
            ["schema"] = Literal("{\"required\":[\"name\",\"age\"]}"),
            ["strict"] = true
        }, new DataBatch());

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SchemaValidationFailed", result.Error?.Code);
        Assert.Contains("age", result.Error?.Message);
    }

    [Fact]
    public async Task ExecuteAsync_MissingRequiredKey_StrictFalse_PassesParseOnly()
    {
        var node = new StructuredOutputNode();
        var context = await BuildContextAsync(node, new()
        {
            ["input"] = Literal("{\"name\":\"Alice\"}"),
            ["schema"] = Literal("{\"required\":[\"name\",\"age\"]}"),
            ["strict"] = false
        }, new DataBatch());

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
    }

    [Fact]
    public async Task ExecuteAsync_TextNotJson_ReturnsInvalidJson()
    {
        var node = new StructuredOutputNode();
        var context = await BuildContextAsync(node, new() { ["input"] = Literal("this is not json") }, new DataBatch());

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("InvalidJson", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_JsonNotObject_ReturnsInvalidJson()
    {
        var node = new StructuredOutputNode();
        var context = await BuildContextAsync(node, new() { ["input"] = Literal("[1,2,3]") }, new DataBatch());

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("InvalidJson", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_InputEmpty_ReturnsMissingInput()
    {
        var node = new StructuredOutputNode();
        var context = await BuildContextAsync(node, new() { ["input"] = Literal("") }, new DataBatch());

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingInput", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_ExpressionJsonText_EvaluatesInputField()
    {
        var node = new StructuredOutputNode();
        var inputBatch = new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = JsonNode.Parse("{\"text\":\"{\\\"name\\\":\\\"Bob\\\",\\\"age\\\":25}\"}"),
                    Success = true
                }
            ]
        };
        var context = await BuildContextAsync(node, new() { ["input"] = (Script)"$json.text" }, inputBatch);

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
        var data = result.Output.Items[0].Data as JsonObject;
        Assert.NotNull(data);
        Assert.Equal("Bob", data!["name"]?.GetValue<string>());
        Assert.Equal(25, data["age"]?.GetValue<int>());
    }

    private static async Task<NodeExecutionContext> BuildContextAsync(INodeType node, Dictionary<string, object> parameters, DataBatch input)
    {
        return await NodeTestContextFactory.BuildAsync(
            node,
            parameters,
            inputs: new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
            {
                [FlowConstants.PortNames.Input] = input
            }).ConfigureAwait(false);
    }

    private static Script Literal(string value)
        => (Script)$"\"{value.Replace("\"", "\\\"")}\"";
}
