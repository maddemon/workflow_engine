using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;

namespace FlowEngine.Runtime.Tests.Plugins;

public class CodeSnippetToolNodeTests
{
    private readonly CodeSnippetToolNode _node = new();

    [Fact]
    public async Task Execute_MissingInput_ReturnsError()
    {
        var context = new NodeExecutionContext
        {
            Node = new NodeDefinition
            {
                Id = Guid.NewGuid(),
                TypeName = "codeSnippetTool",
                Name = "Test CodeSnippet",
                Parameters = [],
                Ports = [],
                ErrorStrategy = ErrorStrategy.Terminate
            },
            ExecutionId = Guid.NewGuid(),
            Inputs = new Dictionary<string, DataBatch>(),
            RawParameters = new Dictionary<string, object>(),
            ResolvedParameters = new Dictionary<string, object>(),
            CancellationToken = CancellationToken.None
        };

        var result = await _node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("MissingInput", result.Error?.Code);
    }

    [Fact]
    public async Task Execute_MissingCode_ReturnsError()
    {
        var context = CreateContext(new JsonObject { ["input"] = "hello" });

        var result = await _node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("MissingCode", result.Error?.Code);
    }

    [Fact]
    public async Task Execute_SimpleCode_ReturnsResult()
    {
        var input = new JsonObject
        {
            ["code"] = "return 42;"
        };
        var context = CreateContext(input);

        var result = await _node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error?.Message);
        var data = result.Output.Items[0].Data;
        Assert.NotNull(data);
        Assert.Equal(42.0, data!.GetValue<double>());
    }

    [Fact]
    public async Task Execute_CodeWithInput_AccessesInput()
    {
        var input = new JsonObject
        {
            ["code"] = "return input.name;",
            ["input"] = new JsonObject { ["name"] = "Alice" }
        };
        var context = CreateContext(input);

        var result = await _node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error?.Message);
        var data = result.Output.Items[0].Data;
        Assert.NotNull(data);
        Assert.Equal("Alice", data!.GetValue<string>());
    }

    [Fact]
    public async Task Execute_CodeReturningObject_ReturnsJsonObject()
    {
        var input = new JsonObject
        {
            ["code"] = "return { message: 'ok', count: 5 };"
        };
        var context = CreateContext(input);

        var result = await _node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error?.Message);
        var json = result.Output.Items[0].Data?.ToJsonString();
        Assert.Contains("\"message\":\"ok\"", json!);
        Assert.Contains("\"count\":5", json);
    }

    [Fact]
    public async Task Execute_ScriptError_ReturnsCodeError()
    {
        var input = new JsonObject
        {
            ["code"] = "throw new Error('test error');"
        };
        var context = CreateContext(input);

        var result = await _node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("CodeError", result.Error?.Code);
    }

    private static NodeExecutionContext CreateContext(JsonObject inputPayload)
    {
        return new NodeExecutionContext
        {
            Node = new NodeDefinition
            {
                Id = Guid.NewGuid(),
                TypeName = "codeSnippetTool",
                Name = "Test CodeSnippet",
                Parameters = [],
                Ports = [],
                ErrorStrategy = ErrorStrategy.Terminate
            },
            ExecutionId = Guid.NewGuid(),
            Inputs = new Dictionary<string, DataBatch>
            {
                ["input"] = new()
                {
                    Items =
                    [
                        new DataItem
                        {
                            Data = inputPayload,
                            Success = true,
                            SourceIndex = 0
                        }
                    ]
                }
            },
            RawParameters = new Dictionary<string, object>(),
            ResolvedParameters = new Dictionary<string, object>(),
            CancellationToken = CancellationToken.None
        };
    }
}
