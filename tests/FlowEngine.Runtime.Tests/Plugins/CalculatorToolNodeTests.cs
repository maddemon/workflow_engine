using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Standard;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// CalculatorToolNode 单元测试，验证 expression / query / math 取值、脚本求值与错误路径。
/// </summary>
public sealed class CalculatorToolNodeTests
{
    private static async Task<NodeExecutionContext> BuildContextAsync(JsonNode? inputData, Dictionary<string, object>? parameters = null)
    {
        var items = inputData is null
            ? new List<DataItem>()
            : new List<DataItem>
            {
                new() { Data = inputData, Success = true, SourceIndex = 0 }
            };

        return await NodeTestContextFactory.BuildAsync(
            new CalculatorToolNode(),
            parameters: parameters,
            inputs: new Dictionary<string, DataBatch>
            {
                [FlowConstants.PortNames.Input] = new DataBatch { Items = items }
            }).ConfigureAwait(false);
    }

    [Fact]
    public async Task ExecuteAsync_ExpressionProperty_EvaluatesAndReturnsResult()
    {
        var context = await BuildContextAsync(JsonNode.Parse("""{"expression":"1 + 2"}"""));

        var result = await ((INodeType)new CalculatorToolNode()).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("1 + 2", result.Output.Items[0].Data?["expression"]?.GetValue<string>());
        Assert.Equal(3d, result.Output.Items[0].Data?["result"]?.GetValue<double>());
    }

    [Fact]
    public async Task ExecuteAsync_QueryProperty_FallsBackToQuery()
    {
        var context = await BuildContextAsync(JsonNode.Parse("""{"query":"10 / 2"}"""));

        var result = await ((INodeType)new CalculatorToolNode()).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("10 / 2", result.Output.Items[0].Data?["expression"]?.GetValue<string>());
        Assert.Equal(5d, result.Output.Items[0].Data?["result"]?.GetValue<double>());
    }

    [Fact]
    public async Task ExecuteAsync_MathProperty_FallsBackToMath()
    {
        var context = await BuildContextAsync(JsonNode.Parse("""{"math":"4 * 2"}"""));

        var result = await ((INodeType)new CalculatorToolNode()).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("4 * 2", result.Output.Items[0].Data?["expression"]?.GetValue<string>());
        Assert.Equal(8d, result.Output.Items[0].Data?["result"]?.GetValue<double>());
    }

    [Fact]
    public async Task ExecuteAsync_ScalarInput_UsesValueAsExpression()
    {
        var context = await BuildContextAsync(JsonValue.Create("7 - 3"));

        var result = await ((INodeType)new CalculatorToolNode()).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("7 - 3", result.Output.Items[0].Data?["expression"]?.GetValue<string>());
        Assert.Equal(4d, result.Output.Items[0].Data?["result"]?.GetValue<double>());
    }

    [Fact]
    public async Task ExecuteAsync_ExpressionProperty_FallsBackWhenNoInput()
    {
        var context = await BuildContextAsync(null);

        var node = new CalculatorToolNode { Expression = "5" };
        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("5", result.Output.Items[0].Data?["expression"]?.GetValue<string>());
        Assert.Equal(5d, result.Output.Items[0].Data?["result"]?.GetValue<double>());
    }

    [Fact]
    public async Task ExecuteAsync_MissingExpression_ReturnsError()
    {
        var context = await BuildContextAsync(JsonNode.Parse("{}"));

        var result = await ((INodeType)new CalculatorToolNode()).ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingExpression", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_ScriptError_ReturnsScriptError()
    {
        var context = await BuildContextAsync(JsonNode.Parse("""{"expression":"1 +"}"""));

        var result = await ((INodeType)new CalculatorToolNode()).ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ScriptError", result.Error?.Code);
    }
}
