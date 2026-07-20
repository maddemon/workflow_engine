using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Registry;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// SubWorkflowExecutor 覆盖测试，通过 SubWorkflowToolNode 间接驱动执行器各分支。
/// </summary>
public sealed class SubWorkflowExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_NoNodeRegistry_ReturnsNoNodeRegistryError()
    {
        var node = new SubWorkflowToolNode
        {
            WorkflowJson = CreateSimpleWorkflowJson("branchResult", new Dictionary<string, object> { ["branchName"] = "ok" })
        };
        var context = CreateContext(node);
        context.NodeRegistry = null;

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("NoNodeRegistry", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_NoEntryNode_ReturnsNoEntryNodeError()
    {
        var workflow = new Workflow
        {
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "n1",
                    TypeName = "branchResult",
                    Name = "n1",
                    Parameters = new Dictionary<string, object> { ["branchName"] = "x" }
                }
            ],
            Connections =
            [
                new Connection
                {
                    SourceNodeId = "missing",
                    SourcePortName = FlowConstants.PortNames.Output,
                    TargetNodeId = "n1",
                    TargetPortName = FlowConstants.PortNames.Input
                }
            ]
        };

        var node = new SubWorkflowToolNode { WorkflowJson = JsonSerializer.Serialize(workflow) };
        var context = CreateContext(node);
        context.NodeRegistry = CreateRegistry();

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("NoEntryNode", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_NodeThrowsException_ReturnsExceptionError()
    {
        var workflow = new Workflow
        {
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "thrower",
                    TypeName = "throwing",
                    Name = "thrower",
                    Parameters = new Dictionary<string, object>()
                }
            ],
            Connections = []
        };

        var node = new SubWorkflowToolNode { WorkflowJson = JsonSerializer.Serialize(workflow) };
        var context = CreateContext(node);
        context.NodeRegistry = CreateRegistry();

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("InvalidOperationException", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_BranchIndexTrue_RoutesToTrueBranch()
    {
        var workflow = new Workflow
        {
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "if",
                    TypeName = "if",
                    Name = "if",
                    Parameters = new Dictionary<string, object> { ["condition"] = "true" }
                },
                new NodeDefinition
                {
                    Id = "trueNode",
                    TypeName = "branchResult",
                    Name = "trueNode",
                    Parameters = new Dictionary<string, object> { ["branchName"] = "true" }
                },
                new NodeDefinition
                {
                    Id = "falseNode",
                    TypeName = "branchResult",
                    Name = "falseNode",
                    Parameters = new Dictionary<string, object> { ["branchName"] = "false" }
                }
            ],
            Connections =
            [
                new Connection
                {
                    SourceNodeId = "if",
                    SourcePortName = FlowConstants.PortNames.True,
                    TargetNodeId = "trueNode",
                    TargetPortName = FlowConstants.PortNames.Input
                },
                new Connection
                {
                    SourceNodeId = "if",
                    SourcePortName = FlowConstants.PortNames.False,
                    TargetNodeId = "falseNode",
                    TargetPortName = FlowConstants.PortNames.Input
                }
            ]
        };

        var node = new SubWorkflowToolNode { WorkflowJson = JsonSerializer.Serialize(workflow) };
        var context = CreateContext(node);
        context.NodeRegistry = CreateRegistry();

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("true", result.Output.Items[0].Data?["branch"]?.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_BranchIndexFalse_RoutesToFalseBranch()
    {
        var workflow = new Workflow
        {
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "if",
                    TypeName = "if",
                    Name = "if",
                    Parameters = new Dictionary<string, object> { ["condition"] = "false" }
                },
                new NodeDefinition
                {
                    Id = "trueNode",
                    TypeName = "branchResult",
                    Name = "trueNode",
                    Parameters = new Dictionary<string, object> { ["branchName"] = "true" }
                },
                new NodeDefinition
                {
                    Id = "falseNode",
                    TypeName = "branchResult",
                    Name = "falseNode",
                    Parameters = new Dictionary<string, object> { ["branchName"] = "false" }
                }
            ],
            Connections =
            [
                new Connection
                {
                    SourceNodeId = "if",
                    SourcePortName = FlowConstants.PortNames.True,
                    TargetNodeId = "trueNode",
                    TargetPortName = FlowConstants.PortNames.Input
                },
                new Connection
                {
                    SourceNodeId = "if",
                    SourcePortName = FlowConstants.PortNames.False,
                    TargetNodeId = "falseNode",
                    TargetPortName = FlowConstants.PortNames.Input
                }
            ]
        };

        var node = new SubWorkflowToolNode { WorkflowJson = JsonSerializer.Serialize(workflow) };
        var context = CreateContext(node);
        context.NodeRegistry = CreateRegistry();

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("false", result.Output.Items[0].Data?["branch"]?.GetValue<string>());
    }

    private static NodeExecutionContext CreateContext(SubWorkflowToolNode node)
    {
        return new NodeExecutionContext
        {
            Node = new NodeDefinition
            {
                Id = "sub",
                TypeName = "subWorkflowTool",
                Name = "sub",
                Parameters = new Dictionary<string, object>
                {
                    ["workflowJson"] = node.WorkflowJson ?? ""
                }
            },
            ExecutionId = Guid.NewGuid(),
            Inputs = new Dictionary<string, DataBatch>
            {
                [FlowConstants.PortNames.Input] = new DataBatch
                {
                    Items =
                    [
                        new DataItem
                        {
                            Data = new JsonObject { ["value"] = 1 },
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
            new INodeType[] { new IfNode(), new BranchResultNode(), new ThrowingNode() },
            NullLogger<NodeRegistry>.Instance);
    }

    private static string CreateSimpleWorkflowJson(string typeName, Dictionary<string, object> parameters)
    {
        var workflow = new Workflow
        {
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "n1",
                    TypeName = typeName,
                    Name = "n1",
                    Parameters = parameters
                }
            ],
            Connections = []
        };
        return JsonSerializer.Serialize(workflow);
    }

    private sealed class BranchResultNode : INodeType
    {
        public string TypeName => "branchResult";
        public string DisplayName => "Branch Result";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports { get; } =
        [
            new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
            new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
        ];
        public bool DefaultIsEntry => false;

        public string BranchName { get; set; } = string.Empty;

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new NodeExecutionResult
            {
                Success = true,
                Output = new DataBatch
                {
                    Items =
                    [
                        new DataItem
                        {
                            Data = new JsonObject { ["branch"] = BranchName },
                            Success = true,
                            SourceIndex = 0
                        }
                    ]
                }
            });
        }
    }

    private sealed class ThrowingNode : INodeType
    {
        public string TypeName => "throwing";
        public string DisplayName => "Throwing";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports { get; } = [];
        public bool DefaultIsEntry => false;

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("boom");
    }
}
