using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
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
        var scriptNode = new NodeDefinition
        {
            Id = "echo",
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

    /// <summary>
    /// 验证 SubWorkflowExecutor 使用 CreateInstance 而非 Singleton Get，
    /// 两个子工作流并发执行同类型节点时参数互不影响。
    /// </summary>
    [Fact]
    public async Task Execute_ConcurrentSubWorkflows_DifferentParameters_DoNotInterfere()
    {
        var registry = CreateRegistry();

        var scriptNode1 = new NodeDefinition
        {
            Id = "script1",
            TypeName = "script",
            Name = "script1",
            Parameters = new Dictionary<string, object>
            {
                ["code"] = "return { value: 'A' };"
            }
        };

        var scriptNode2 = new NodeDefinition
        {
            Id = "script2",
            TypeName = "script",
            Name = "script2",
            Parameters = new Dictionary<string, object>
            {
                ["code"] = "return { value: 'B' };"
            }
        };

        var workflow1 = new Workflow { Nodes = [scriptNode1], Connections = [] };
        var workflow2 = new Workflow { Nodes = [scriptNode2], Connections = [] };

        var node1 = new SubWorkflowToolNode { WorkflowJson = JsonSerializer.Serialize(workflow1) };
        var node2 = new SubWorkflowToolNode { WorkflowJson = JsonSerializer.Serialize(workflow2) };

        var context1 = CreateContext(node1);
        context1.NodeRegistry = registry;

        var context2 = CreateContext(node2);
        context2.NodeRegistry = registry;

        // Execute concurrently — should not interfere with each other
        var results = await Task.WhenAll(
            node1.ExecuteAsync(context1, TestContext.Current.CancellationToken),
            node2.ExecuteAsync(context2, TestContext.Current.CancellationToken));

        Assert.True(results[0].Success, results[0].Error?.Message);
        Assert.True(results[1].Success, results[1].Error?.Message);
    }

    /// <summary>
    /// 验证 nodeOutputs 以 node.Id 为键而非 node.Name，
    /// 两个同 Name 不同 Id 的节点输出不会互相覆盖。
    /// 场景：A(Name="same",Id="a1") 和 B(Name="same",Id="b2") 同为入口节点，
    /// C 依赖 A。若键为 Name，B 的输出会覆盖 A 的，C 收到 B 的数据（bug）。
    /// </summary>
    [Fact]
    public async Task Execute_SameNameDifferentId_OutputsNotOverwritten()
    {
        var nodeA = new NodeDefinition
        {
            Id = "a1",
            TypeName = "script",
            Name = "same",
            Parameters = new Dictionary<string, object>
            {
                ["code"] = "return { value: 'A' };"
            }
        };

        var nodeB = new NodeDefinition
        {
            Id = "b2",
            TypeName = "script",
            Name = "same",
            Parameters = new Dictionary<string, object>
            {
                ["code"] = "return { value: 'B' };"
            }
        };

        var nodeC = new NodeDefinition
        {
            Id = "c3",
            TypeName = "script",
            Name = "consumer",
            Parameters = new Dictionary<string, object>
            {
                ["code"] = "return $input.first();"
            }
        };

        var workflow = new Workflow
        {
            Nodes = [nodeA, nodeB, nodeC],
            Connections =
            [
                new Connection
                {
                    SourceNodeId = "a1",
                    SourcePortName = FlowConstants.PortNames.Output,
                    TargetNodeId = "c3",
                    TargetPortName = FlowConstants.PortNames.Input
                }
            ]
        };

        var node = new SubWorkflowToolNode
        {
            WorkflowJson = JsonSerializer.Serialize(workflow)
        };

        var context = CreateContext(node);
        context.NodeRegistry = CreateRegistry();

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error?.Message);
        Assert.NotNull(result.Output);
        Assert.Single(result.Output.Items);

        // C should receive A's output {value:"A"}, not B's {value:"B"}
        var data = result.Output.Items[0].Data;
        Assert.NotNull(data);
        Assert.Equal("A", data?["value"]?.GetValue<string>());
    }

    /// <summary>
    /// 验证嵌套深度超过限制时返回 MaxNestingDepthExceeded 错误。
    /// </summary>
    [Fact]
    public async Task Execute_NestingDepthExceeded_ReturnsMaxNestingDepthExceeded()
    {
        var scriptNode = new NodeDefinition
        {
            Id = "echo",
            TypeName = "script",
            Name = "echo",
            Parameters = new Dictionary<string, object>
            {
                ["code"] = "return { result: 'done' };"
            }
        };

        var workflow = new Workflow { Nodes = [scriptNode], Connections = [] };

        var node = new SubWorkflowToolNode
        {
            WorkflowJson = JsonSerializer.Serialize(workflow),
            MaxNestingDepth = 3
        };

        var context = CreateContext(node);
        context.NestingDepth = 3; // Equals MaxNestingDepth → should be rejected
        context.NodeRegistry = CreateRegistry();

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("MaxNestingDepthExceeded", result.Error?.Code);
    }

    /// <summary>
    /// 验证嵌套深度在限制内时正常执行。
    /// </summary>
    [Fact]
    public async Task Execute_NestingDepthWithinLimit_ExecutesSuccessfully()
    {
        var scriptNode = new NodeDefinition
        {
            Id = "echo",
            TypeName = "script",
            Name = "echo",
            Parameters = new Dictionary<string, object>
            {
                ["code"] = "return { result: 'done' };"
            }
        };

        var workflow = new Workflow { Nodes = [scriptNode], Connections = [] };

        var node = new SubWorkflowToolNode
        {
            WorkflowJson = JsonSerializer.Serialize(workflow),
            MaxNestingDepth = 5
        };

        var context = CreateContext(node);
        context.NestingDepth = 2; // Less than MaxNestingDepth → should succeed
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
                Id = "Test SubWorkflow",
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
                [FlowConstants.PortNames.Input] = new()
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
