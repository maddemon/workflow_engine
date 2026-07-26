using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Registry;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// §5.4 隔离性：子工作流执行器（<see cref="SubWorkflowExecutor"/>）为内部节点构造独立的
/// <see cref="NodeExecutionContext"/>，其 <c>NodeContext</c> 与父上下文相互独立，互不污染。
/// </summary>
public sealed class SubWorkflowNodeContextIsolationTests
{
    [Fact]
    public async Task ExecuteAsync_InnerNodeContext_IsolatedFromParent()
    {
        // 父上下文携带自有节点上下文（含 parentKey）。
        var parentContextDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["parentKey"] = "parentVal"
        };

        var workflow = new Workflow
        {
            Nodes = [new NodeDefinition { Id = "w", TypeName = "ctxWriter", Name = "w", IsEntry = true }],
            Connections = []
        };

        var node = new SubWorkflowToolNode { WorkflowJson = JsonSerializer.Serialize(workflow) };
        var context = new NodeExecutionContext
        {
            Node = new NodeDefinition
            {
                Id = "sub",
                TypeName = "subWorkflowTool",
                Name = "sub",
                Parameters = new Dictionary<string, object> { ["workflowJson"] = node.WorkflowJson ?? "" }
            },
            ExecutionId = Guid.NewGuid(),
            Inputs = new Dictionary<string, DataBatch>
            {
                [FlowConstants.PortNames.Input] = new DataBatch
                {
                    Items = [new DataItem { Data = new JsonObject { ["value"] = 1 }, Success = true, SourceIndex = 0 }]
                }
            },
            RawParameters = new Dictionary<string, object> { ["workflowJson"] = node.WorkflowJson ?? "" },
            ResolvedParameters = new Dictionary<string, object> { ["workflowJson"] = node.WorkflowJson ?? "" },
            CancellationToken = CancellationToken.None,
            NodeContext = parentContextDict
        };
        context.NodeRegistry = new NodeRegistry(new INodeType[] { new ContextWriterNode() }, NullLogger<NodeRegistry>.Instance);

        var result = await ((INodeType)node).ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        // 父上下文未被子工作流内部节点污染：仍仅有 parentKey，不含内部节点写入的 innerKey。
        Assert.True(parentContextDict.ContainsKey("parentKey"));
        Assert.False(parentContextDict.ContainsKey("innerKey"));
    }

    /// <summary>
    /// 内部节点：向自身节点上下文写入 innerKey，用于验证其与父上下文隔离。
    /// </summary>
    private sealed class ContextWriterNode : INodeType
    {
        public string TypeName => "ctxWriter";
        public string DisplayName => "Ctx Writer";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports { get; } =
        [
            new PortDefinition { Name = FlowConstants.PortNames.Input, Direction = PortDirection.Input, Type = PortType.Main },
            new PortDefinition { Name = FlowConstants.PortNames.Output, Direction = PortDirection.Output, Type = PortType.Main }
        ];
        public bool DefaultIsEntry => false;

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
        {
            context.NodeContext["innerKey"] = true;
            return Task.FromResult(context.Ok(new JsonObject { ["written"] = true }));
        }
    }
}
