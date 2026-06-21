using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Runtime.Tests.Plugins;

public class SubWorkflowToolNodeTests
{
    [Fact]
    public async Task Execute_EmptyWorkflowJson_ReturnsError()
    {
        var node = new SubWorkflowToolNode { WorkflowJson = "" };
        var context = CreateContext(node);

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("MissingWorkflowJson", result.Error?.Code);
    }

    [Fact]
    public async Task Execute_InvalidJson_ReturnsError()
    {
        var node = new SubWorkflowToolNode { WorkflowJson = "not json" };
        var context = CreateContext(node);

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("InvalidWorkflowJson", result.Error?.Code);
    }

    [Fact]
    public async Task Execute_EmptyWorkflow_ReturnsError()
    {
        var workflow = new Workflow { Nodes = [], Connections = [] };
        var node = new SubWorkflowToolNode
        {
            WorkflowJson = JsonSerializer.Serialize(workflow)
        };
        var context = CreateContext(node);

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("EmptyWorkflow", result.Error?.Code);
    }

    [Fact]
    public async Task Execute_SimpleWorkflow_ExecutesSuccessfully()
    {
        var scriptNode = new NodeInstance
        {
            Id = Guid.NewGuid(),
            TypeName = "script",
            Name = "echo",
            Parameters = new Dictionary<string, object>
            {
                ["code"] = "return { result: 'done' };"
            }
        };

        var workflow = new Workflow
        {
            Nodes = [scriptNode],
            Connections = []
        };

        var node = new SubWorkflowToolNode
        {
            WorkflowJson = JsonSerializer.Serialize(workflow)
        };

        var context = CreateContext(node);
        context.NodeRegistry = CreateRegistry();

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error?.Message);
    }

    private static NodeExecutionContext CreateContext(SubWorkflowToolNode node)
    {
        return new NodeExecutionContext
        {
            Node = new NodeDefinition
            {
                Id = Guid.NewGuid(),
                TypeName = "subWorkflowTool",
                Name = "Test SubWorkflow",
                Parameters = new Dictionary<string, object>
                {
                    ["workflowJson"] = node.WorkflowJson ?? ""
                },
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
                            Data = new JsonObject(),
                            Success = true,
                            SourceIndex = 0
                        }
                    ]
                }
            },
            RawParameters = new Dictionary<string, object> { ["workflowJson"] = node.WorkflowJson ?? "" },
            ResolvedParameters = new Dictionary<string, object> { ["workflowJson"] = node.WorkflowJson ?? "" },
            CancellationToken = CancellationToken.None
        };
    }

    private static INodeRegistry CreateRegistry()
    {
        return new NodeRegistry(
            new INodeType[] { new JSNode() },
            NullLogger<NodeRegistry>.Instance);
    }
}
