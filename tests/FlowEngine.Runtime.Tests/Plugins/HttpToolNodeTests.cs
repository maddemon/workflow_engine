using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;

namespace FlowEngine.Runtime.Tests.Plugins;

public class HttpToolNodeTests
{
    private readonly HttpToolNode _node = new();

    [Fact]
    public async Task Execute_MissingInput_ReturnsError()
    {
        var context = new NodeExecutionContext
        {
            Node = new NodeDefinition
            {
                Id = Guid.NewGuid(),
                TypeName = "httpTool",
                Name = "Test Http",
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
    public async Task Execute_MissingUrl_ReturnsError()
    {
        var context = CreateContext(new JsonObject { ["method"] = "GET" });

        var result = await _node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("MissingUrl", result.Error?.Code);
    }

    [Fact]
    public async Task Execute_InvalidUrl_ReturnsError()
    {
        var context = CreateContext(new JsonObject
        {
            ["url"] = "http://localhost:99999/nonexistent",
            ["method"] = "GET"
        });

        var result = await _node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
    }

    [Fact]
    public void ToolNode_HasCorrectMetadata()
    {
        Assert.Equal("httpTool", _node.TypeName);
        Assert.Equal("HTTP Tool", _node.DisplayName);
        Assert.Equal("Tool", _node.Category);
        Assert.False(_node.DefaultIsEntry);
    }

    [Fact]
    public void ToolNode_HasInputAndOutputPorts()
    {
        Assert.Equal(2, _node.Ports.Count);
        Assert.Contains(_node.Ports, p => p.Name == "input" && p.Direction == PortDirection.Input);
        Assert.Contains(_node.Ports, p => p.Name == "output" && p.Direction == PortDirection.Output);
    }

    private static NodeExecutionContext CreateContext(JsonObject inputPayload)
    {
        return new NodeExecutionContext
        {
            Node = new NodeDefinition
            {
                Id = Guid.NewGuid(),
                TypeName = "httpTool",
                Name = "Test Http",
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
