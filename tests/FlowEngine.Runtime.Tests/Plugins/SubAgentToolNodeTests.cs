using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Agent;
using FlowEngine.Core.Dtos;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Registry;
using FlowEngine.Runtime.Tests.Executor;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Runtime.Tests.Plugins;

public class SubAgentToolNodeTests
{
    private readonly INodeRegistry _nodeRegistry;

    public SubAgentToolNodeTests()
    {
        _nodeRegistry = new NodeRegistry(
            new INodeType[]
            {
                new PassThroughNode(),
                new AgentNode(),
                new SubAgentToolNode()
            },
            NullLogger<NodeRegistry>.Instance);
    }

    [Fact]
    public void SubAgentToolNode_Has_Correct_TypeName()
    {
        var node = new SubAgentToolNode();
        Assert.Equal("subAgentTool", node.TypeName);
    }

    [Fact]
    public void SubAgentToolNode_Has_Correct_Ports()
    {
        var node = new SubAgentToolNode();

        Assert.Equal(4, node.Ports.Count);
        Assert.Contains(node.Ports, p => p.Name == FlowConstants.PortNames.Input && p.Type == PortType.Main && p.Direction == PortDirection.Input);
        Assert.Contains(node.Ports, p => p.Name == FlowConstants.PortNames.Output && p.Type == PortType.Main && p.Direction == PortDirection.Output);
        Assert.Contains(node.Ports, p => p.Name == FlowConstants.PortNames.Tools && p.Type == PortType.AgentTool && p.Direction == PortDirection.Input);
        Assert.Contains(node.Ports, p => p.Name == FlowConstants.PortNames.Llm && p.Type == PortType.LLM && p.Direction == PortDirection.Input);
    }

    [Fact]
    public void SubAgentToolNode_Default_Parameters()
    {
        var node = new SubAgentToolNode();
        Assert.Equal(string.Empty, node.PromptTemplate);
        Assert.Equal(3, node.MaxNestingDepth);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Error_When_MaxNestingDepth_Exceeded()
    {
        var node = new SubAgentToolNode { MaxNestingDepth = 2 };
        var context = CreateContext(nestingDepth: 2);

        var result = await node.ExecuteAsync(context);

        Assert.False(result.Success);
        Assert.Equal("MaxNestingDepthExceeded", result.Error?.Code);
        Assert.Contains("exceeds maximum", result.Error!.Message);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Error_When_No_LlmClient()
    {
        var node = new SubAgentToolNode();
        var context = CreateContext(llmClient: null);

        var result = await node.ExecuteAsync(context);

        Assert.False(result.Success);
        Assert.Equal("MissingLlmClient", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_Calls_LLM_And_Returns_Result()
    {
        var node = new SubAgentToolNode
        {
            PromptTemplate = "You are a helper."
        };

        var llmClient = new MockLlmClient(_ => new LlmResponse { Content = "Nested agent response" });

        var inputBatch = new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = JsonNode.Parse("{\"task\": \"do something\"}"),
                    Success = true,
                    SourceIndex = 0
                }
            ]
        };

        var context = CreateContext(
            llmClient: llmClient,
            inputs: new Dictionary<string, DataBatch> { [FlowConstants.PortNames.Input] = inputBatch });

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.NotNull(result.Output.Items);
        Assert.Single(result.Output.Items);
    }

    [Fact]
    public async Task ExecuteAsync_Allows_Depth_Below_Max()
    {
        var node = new SubAgentToolNode { MaxNestingDepth = 3 };
        var context = CreateContext(nestingDepth: 1);

        var llmClient = new MockLlmClient(_ => new LlmResponse { Content = "OK" });
        context.LlmClient = llmClient;

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_Passes_NodeExecutionRecordId_As_ParentRecordId_When_Available()
    {
        var toolNode = CreateNodeDefinition("tool1", "passThrough");
        var subAgentNode = CreateNodeDefinition("subAgent1", "subAgentTool");

        var workflow = CreateWorkflow(toolNode, subAgentNode);

        var executionId = Guid.NewGuid();
        var callCount = 0;
        var llmClient = new MockLlmClient(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new LlmResponse
                {
                    Content = null,
                    ToolCalls =
                    [
                        new LlmToolCall
                        {
                            Id = "call1",
                            Name = "tool1",
                            Arguments = "{\"value\": 42}"
                        }
                    ]
                };
            }

            return new LlmResponse { Content = "Done" };
        });

        var node = new SubAgentToolNode
        {
            PromptTemplate = "You are a helper."
        };

        var nodeExecutionRecordId = Guid.NewGuid();
        var context = CreateContext(
            workflow: workflow,
            llmClient: llmClient,
            nodeExecutionRecordId: nodeExecutionRecordId,
            executionId: executionId);

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.Equal(2, callCount);
        Assert.Single(result.Output.Items);

        // Verify DTO consistency with AgentNode
        var dto = result.Output.Items[0].Data?.Deserialize<AgentExecutionResultDto>(JsonDefaults.Options);
        Assert.NotNull(dto);
        Assert.Equal("Completed", dto.AgentInfo.Status);
        Assert.Equal("test-model", dto.AgentInfo.Model);
        Assert.NotNull(dto.AgentInfo.CompletedAt);

        // SubAgentToolNode 解析 parentRecordId = NodeExecutionRecordId != Guid.Empty ? NodeExecutionRecordId : ExecutionId。
        // 此处 NodeExecutionRecordId 已设置，parentRecordId == nodeExecutionRecordId。
        // InlineResolver 将 parentRecordId 透传到 ToolExecutionRecords，此处直接验证透传链路。
        var resolverCallCount = 0;
        var resolverLlmClient = new MockLlmClient(_ =>
        {
            resolverCallCount++;
            if (resolverCallCount == 1)
            {
                return new LlmResponse
                {
                    Content = null,
                    ToolCalls =
                    [
                        new LlmToolCall { Id = "r-call1", Name = "tool1", Arguments = "{}" }
                    ]
                };
            }

            return new LlmResponse { Content = "Done" };
        });

        var tools = new List<ToolDefinition>
        {
            new() { Name = "tool1", Description = "test", TargetNodeDefinitionId = toolNode.Id }
        };

        var resolver = new InlineResolver(
            resolverLlmClient,
            tools,
            context,
            maxIterations: 3,
            parentRecordId: nodeExecutionRecordId);

        var resolverMessages = new List<LlmMessage> { new() { Role = "user", Content = "Test" } };
        var resolverResult = await resolver.RunAsync(resolverMessages);

        Assert.NotEmpty(resolverResult.ToolExecutionRecords);
        Assert.All(resolverResult.ToolExecutionRecords, r => Assert.Equal(nodeExecutionRecordId, r.ParentRecordId));
    }

    [Fact]
    public async Task ExecuteAsync_Falls_Back_To_ExecutionId_When_NodeExecutionRecordId_Is_Empty()
    {
        var toolNode = CreateNodeDefinition("tool1", "passThrough");
        var subAgentNode = CreateNodeDefinition("subAgent1", "subAgentTool");

        var workflow = CreateWorkflow(toolNode, subAgentNode);

        var executionId = Guid.NewGuid();
        var callCount = 0;
        var llmClient = new MockLlmClient(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new LlmResponse
                {
                    Content = null,
                    ToolCalls =
                    [
                        new LlmToolCall
                        {
                            Id = "call1",
                            Name = "tool1",
                            Arguments = "{\"value\": 42}"
                        }
                    ]
                };
            }

            return new LlmResponse { Content = "Done" };
        });

        var node = new SubAgentToolNode
        {
            PromptTemplate = "You are a helper."
        };

        // NodeExecutionRecordId 为 Guid.Empty，SubAgentToolNode 回退到 ExecutionId 作为 parentRecordId。
        var context = CreateContext(
            workflow: workflow,
            llmClient: llmClient,
            nodeExecutionRecordId: Guid.Empty,
            executionId: executionId);

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.Equal(2, callCount);

        // 验证回退逻辑：parentRecordId == executionId（因 NodeExecutionRecordId == Guid.Empty）。
        var resolverCallCount = 0;
        var resolverLlmClient = new MockLlmClient(_ =>
        {
            resolverCallCount++;
            if (resolverCallCount == 1)
            {
                return new LlmResponse
                {
                    Content = null,
                    ToolCalls =
                    [
                        new LlmToolCall { Id = "r-call1", Name = "tool1", Arguments = "{}" }
                    ]
                };
            }

            return new LlmResponse { Content = "Done" };
        });

        var tools = new List<ToolDefinition>
        {
            new() { Name = "tool1", Description = "test", TargetNodeDefinitionId = toolNode.Id }
        };

        var resolver = new InlineResolver(
            resolverLlmClient,
            tools,
            context,
            maxIterations: 3,
            parentRecordId: executionId);

        var resolverMessages = new List<LlmMessage> { new() { Role = "user", Content = "Test" } };
        var resolverResult = await resolver.RunAsync(resolverMessages);

        Assert.NotEmpty(resolverResult.ToolExecutionRecords);
        Assert.All(resolverResult.ToolExecutionRecords, r => Assert.Equal(executionId, r.ParentRecordId));
    }

    [Fact]
    public async Task ExecuteAsync_MemoryEnabled_Completes_MultiIteration_Flow()
    {
        var toolNode = CreateNodeDefinition("tool1", "passThrough");
        var subAgentNode = CreateNodeDefinition("subAgent1", "subAgentTool");

        var workflow = CreateWorkflow(toolNode, subAgentNode);

        var callCount = 0;
        var llmClient = new MockLlmClient(_ =>
        {
            callCount++;
            if (callCount <= 2)
            {
                return new LlmResponse
                {
                    Content = null,
                    ToolCalls =
                    [
                        new LlmToolCall { Id = $"call{callCount}", Name = "tool1", Arguments = "{}" }
                    ]
                };
            }

            return new LlmResponse { Content = "Done" };
        });

        var node = new SubAgentToolNode
        {
            PromptTemplate = "You are a helper.",
            MemoryEnabled = true,
            MemoryWindowSize = 10
        };

        var context = CreateContext(workflow: workflow, llmClient: llmClient);

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.Equal(3, callCount);
    }

    private NodeExecutionContext CreateContext(
        Workflow? workflow = null,
        ILlmClient? llmClient = null,
        int nestingDepth = 0,
        IReadOnlyDictionary<string, DataBatch>? inputs = null,
        Guid? nodeExecutionRecordId = null,
        Guid? executionId = null)
    {
        return new NodeExecutionContext
        {
            Workflow = workflow ?? new Workflow
            {
                Id = Guid.NewGuid(),
                Name = "test",
                CreatedBy = "test",
                Nodes = [],
                Connections = []
            },
            ExecutionId = executionId ?? Guid.NewGuid(),
            NodeExecutionRecordId = nodeExecutionRecordId ?? Guid.Empty,
            Node = new NodeDefinition
            {
                Id = "subAgent1",
                TypeName = "subAgentTool",
                Name = "subAgent1",
                Parameters = []
            },
            Inputs = inputs ?? new Dictionary<string, DataBatch>(),
            RawParameters = new Dictionary<string, object>(),
            ResolvedParameters = new Dictionary<string, object>(),
            Credentials = new TestCredentialAccessor(),
            Logger = NullExecutionLogger.Instance,
            CancellationToken = CancellationToken.None,
            LlmClient = llmClient,
            NodeRegistry = _nodeRegistry,
            NestingDepth = nestingDepth
        };
    }

    private static Workflow CreateWorkflow(params NodeDefinition[] nodes)
    {
        return new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "test",
            CreatedBy = "test",
            Nodes = nodes.ToList(),
            Connections = nodes.Length > 1
                ?
                [
                    new Connection
                    {
                        Id = Guid.NewGuid(),
                        SourceNodeId = nodes[0].Id,
                        SourcePortName = FlowConstants.PortNames.Output,
                        TargetNodeId = nodes[1].Id,
                        TargetPortName = FlowConstants.PortNames.Tools
                    }
                ]
                : []
        };
    }

    private static NodeDefinition CreateNodeDefinition(string name, string typeName)
    {
        return new NodeDefinition
        {
            Id = name,
            Name = name,
            TypeName = typeName,
            Parameters = []
        };
    }
}
