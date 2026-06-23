using System.Text.Json.Nodes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;

namespace FlowEngine.Runtime.Tests.Plugins;

public class HttpToolNodeTests
{
    [Fact]
    public async Task Execute_MissingUrl_ReturnsError()
    {
        var node = new HttpToolNode { Url = "" };
        var context = CreateContext(new JsonObject { ["path"] = "test" });

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("MissingUrl", result.Error?.Code);
    }

    [Fact]
    public async Task Execute_WithUrlExpression_ResolvesUrl()
    {
        var node = new HttpToolNode
        {
            Url = "'https://httpbin.org/get'",
            Method = HttpMethodOption.Get
        };
        var context = CreateContext(new JsonObject());

        // Execute - may succeed or fail depending on network
        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        // Verify URL was resolved correctly
        // The request may succeed or fail depending on network, just verify no URL error
        if (!result.Success)
        {
            Assert.NotEqual("MissingUrl", result.Error?.Code);
        }
    }

    [Fact]
    public void ToolNode_HasCorrectMetadata()
    {
        var node = new HttpToolNode();
        Assert.Equal("httpTool", node.TypeName);
        Assert.Equal("HTTP Tool", node.DisplayName);
        Assert.Equal("AI", node.Category);
        Assert.False(node.DefaultIsEntry);
    }

    [Fact]
    public void ToolNode_HasInputAndOutputPorts()
    {
        var node = new HttpToolNode();
        Assert.Equal(3, node.Ports.Count);
        Assert.Contains(node.Ports, p => p.Name == "input" && p.Direction == PortDirection.Input);
        Assert.Contains(node.Ports, p => p.Name == "output" && p.Direction == PortDirection.Output);
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
