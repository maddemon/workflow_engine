using System.Text.Json;
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

namespace FlowEngine.Runtime.Tests.Agent;

public class SubAgentToolNodeTests
{
    private readonly INodeRegistry _nodeRegistry;

    public SubAgentToolNodeTests()
    {
        _nodeRegistry = new NodeRegistry(
            new INodeType[]
            {
                new PassThroughNode(),
                new SubAgentToolNode()
            },
            NullLogger<NodeRegistry>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_Passes_NodeExecutionRecordId_As_ParentRecordId_When_Available()
    {
        var toolNode = CreateNodeDefinition("tool1", "passThrough");
        var subAgentNode = CreateNodeDefinition("subAgent1", "subAgentTool");

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "test",
            CreatedBy = "test",
            Nodes = [subAgentNode, toolNode],
            Connections =
            [
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = toolNode.Id,
                    SourcePortName = FlowConstants.PortNames.Output,
                    TargetNodeId = subAgentNode.Id,
                    TargetPortName = FlowConstants.PortNames.Tools
                }
            ]
        };

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
        var context = new NodeExecutionContext
        {
            Workflow = workflow,
            ExecutionId = executionId,
            NodeExecutionRecordId = nodeExecutionRecordId,
            Node = subAgentNode,
            Inputs = new Dictionary<string, DataBatch>(),
            RawParameters = new Dictionary<string, object>(),
            ResolvedParameters = new Dictionary<string, object>(),
            Credentials = new TestCredentialAccessor(),
            Logger = NullExecutionLogger.Instance,
            CancellationToken = CancellationToken.None,
            LlmClient = llmClient,
            NodeRegistry = _nodeRegistry
        };

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

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "test",
            CreatedBy = "test",
            Nodes = [subAgentNode, toolNode],
            Connections =
            [
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = toolNode.Id,
                    SourcePortName = FlowConstants.PortNames.Output,
                    TargetNodeId = subAgentNode.Id,
                    TargetPortName = FlowConstants.PortNames.Tools
                }
            ]
        };

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
        var context = new NodeExecutionContext
        {
            Workflow = workflow,
            ExecutionId = executionId,
            NodeExecutionRecordId = Guid.Empty,
            Node = subAgentNode,
            Inputs = new Dictionary<string, DataBatch>(),
            RawParameters = new Dictionary<string, object>(),
            ResolvedParameters = new Dictionary<string, object>(),
            Credentials = new TestCredentialAccessor(),
            Logger = NullExecutionLogger.Instance,
            CancellationToken = CancellationToken.None,
            LlmClient = llmClient,
            NodeRegistry = _nodeRegistry
        };

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

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "test",
            CreatedBy = "test",
            Nodes = [subAgentNode, toolNode],
            Connections =
            [
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = toolNode.Id,
                    SourcePortName = FlowConstants.PortNames.Output,
                    TargetNodeId = subAgentNode.Id,
                    TargetPortName = FlowConstants.PortNames.Tools
                }
            ]
        };

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

        var context = new NodeExecutionContext
        {
            Workflow = workflow,
            ExecutionId = Guid.NewGuid(),
            Node = subAgentNode,
            Inputs = new Dictionary<string, DataBatch>(),
            RawParameters = new Dictionary<string, object>(),
            ResolvedParameters = new Dictionary<string, object>(),
            Credentials = new TestCredentialAccessor(),
            Logger = NullExecutionLogger.Instance,
            CancellationToken = CancellationToken.None,
            LlmClient = llmClient,
            NodeRegistry = _nodeRegistry
        };

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.Equal(3, callCount);
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

    private sealed class MockLlmClient : ILlmClient
    {
        private readonly Func<IReadOnlyList<ToolDefinition>, LlmResponse> _responder;

        public string ModelName => "test-model";

        public MockLlmClient(Func<IReadOnlyList<ToolDefinition>, LlmResponse> responder)
        {
            _responder = responder;
        }

        public Task<LlmResponse> ChatAsync(
            IReadOnlyList<LlmMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_responder(tools));
        }
    }

    private sealed class TestCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult(new CredentialValue());
    }

    private sealed class NullExecutionLogger : IExecutionLogger
    {
        public static readonly NullExecutionLogger Instance = new();

        public void LogInformation(string message, params object?[] args) { }
        public void LogWarning(string message, params object?[] args) { }
        public void LogError(Exception? exception, string message, params object?[] args) { }
    }
}
