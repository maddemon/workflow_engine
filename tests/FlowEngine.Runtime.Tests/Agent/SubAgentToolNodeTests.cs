using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
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
        Assert.Equal("Done", result.Output.Items[0].Data?.ToString());
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
