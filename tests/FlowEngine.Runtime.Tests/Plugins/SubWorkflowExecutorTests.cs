using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
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

    [Fact]
    public async Task ExecuteAsync_MultiParentNode_MergesAllInboundInputs()
    {
        // 两个父节点 p1、p2 各输出单条数据，均连接到目标节点 t 的 input 端口；
        // 修复前第二条入边会被 executed 跳过而仅保留第一条父节点的数据。
        var workflow = new Workflow
        {
            Nodes =
            [
                new NodeDefinition { Id = "p1", TypeName = "branchResult", Name = "p1", Parameters = new Dictionary<string, object> { ["branchName"] = "p1" } },
                new NodeDefinition { Id = "p2", TypeName = "branchResult", Name = "p2", Parameters = new Dictionary<string, object> { ["branchName"] = "p2" } },
                new NodeDefinition { Id = "t", TypeName = "captureInput", Name = "t", Parameters = new Dictionary<string, object>() }
            ],
            Connections =
            [
                new Connection { SourceNodeId = "p1", SourcePortName = FlowConstants.PortNames.Output, TargetNodeId = "t", TargetPortName = FlowConstants.PortNames.Input },
                new Connection { SourceNodeId = "p2", SourcePortName = FlowConstants.PortNames.Output, TargetNodeId = "t", TargetPortName = FlowConstants.PortNames.Input }
            ]
        };

        var node = new SubWorkflowToolNode { WorkflowJson = JsonSerializer.Serialize(workflow) };
        var context = CreateContext(node);
        context.NodeRegistry = CreateRegistry();

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = result.Output.Items[0].Data!;
        // 合并后应包含两个父节点的数据，且 SourceIndex 重排为 0、1。
        Assert.Equal(2, data["count"]?.GetValue<int>());
        Assert.Equal(0, data["idx0"]?.GetValue<int>());
        Assert.Equal(1, data["idx1"]?.GetValue<int>());
    }

    [Fact]
    public async Task ExecuteAsync_InnerNodeExpressionParameter_PreResolvedBeforeExecution()
    {
        // 内部节点含 Expression 类型参数，执行前应由 Core 级预求值写入 ResolvedValue。
        var workflow = new Workflow
        {
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "expr",
                    TypeName = "exprNode",
                    Name = "expr",
                    IsEntry = true,
                    Parameters = new Dictionary<string, object> { ["expr"] = (Script)"1 + 2" }
                }
            ],
            Connections = []
        };

        var node = new SubWorkflowToolNode { WorkflowJson = JsonSerializer.Serialize(workflow) };
        var context = CreateContext(node);
        context.NodeRegistry = CreateRegistry();

        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = result.Output.Items[0].Data!;
        Assert.Equal(3, data["resolved"]?.GetValue<int>());
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
            new INodeType[] { new IfNode(), new BranchResultNode(), new ThrowingNode(), new CaptureInputNode(), new ExprNode() },
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

    /// <summary>
    /// 捕获 input 端口的合并批：将项数与 SourceIndex 回写到输出，便于断言多入边合并结果。
    /// </summary>
    private sealed class CaptureInputNode : INodeType
    {
        public string TypeName => "captureInput";
        public string DisplayName => "Capture Input";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports { get; } =
        [
            new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
            new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
        ];
        public bool DefaultIsEntry => false;

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
        {
            var count = context.Inputs.TryGetValue(FlowConstants.PortNames.Input, out var batch) ? batch.Items.Count : 0;
            var indexes = batch?.Items.Select(i => i.SourceIndex).ToArray() ?? [];
            return Task.FromResult(context.Ok(new JsonObject
            {
                ["count"] = count,
                ["idx0"] = indexes.Length > 0 ? indexes[0] : -1,
                ["idx1"] = indexes.Length > 1 ? indexes[1] : -1
            }));
        }
    }

    /// <summary>
    /// 含 Expression 类型参数 expr 的节点，执行时将预求值结果写回输出，用于断言预求值已发生。
    /// </summary>
    private sealed class ExprNode : INodeType
    {
        public string TypeName => "exprNode";
        public string DisplayName => "Expr Node";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports { get; } =
        [
            new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
            new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
        ];
        public bool DefaultIsEntry => false;

        [Hint(PresentationHint.Expression)]
        public Script Expr { get; set; } = "1 + 2";

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
        {
            var resolved = context.RawParameters.TryGetValue("expr", out var raw)
                && raw is Script script
                && script.ResolvedValue is not null
                ? script.ResolvedValue.GetValue<int>()
                : -1;
            return Task.FromResult(context.Ok(new JsonObject { ["resolved"] = resolved }));
        }
    }
}
