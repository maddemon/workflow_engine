using System.Text.Json;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Dtos;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Registry;
using FlowEngine.Runtime.Tests.Executor;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Runtime.Tests.Agent;

public class AgentNodeDtoTests
{
    private readonly INodeRegistry _nodeRegistry;

    public AgentNodeDtoTests()
    {
        _nodeRegistry = new NodeRegistry(
            new INodeType[]
            {
                new PassThroughNode(),
                new AgentNode()
            },
            NullLogger<NodeRegistry>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_Output_Deserializes_To_AgentExecutionResultDto()
    {
        var node = new AgentNode();
        var llmClient = new MockLlmClient(_ => new LlmResponse { Content = "Done" });
        var workflow = CreateWorkflow();
        var context = CreateContext(workflow: workflow, llmClient: llmClient);

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.Single(result.Output.Items);

        var data = result.Output.Items[0].Data;
        Assert.NotNull(data);

        var dto = JsonSerializer.Deserialize<AgentExecutionResultDto>(data.ToJsonString(), JsonDefaults.Options);
        Assert.NotNull(dto);
        Assert.NotNull(dto.AgentInfo);
        Assert.Equal("Completed", dto.AgentInfo.Status);
        Assert.Equal(1, dto.AgentInfo.IterationCount);
        Assert.Single(dto.Iterations);
        Assert.Single(dto.Iterations[0].LlmChunks);
        Assert.Equal("Done", dto.Iterations[0].LlmChunks[0].Content);
        Assert.Empty(dto.SubRecords);
    }

    [Fact]
    public async Task ExecuteAsync_MaxIterations_Output_Deserializes_To_Failed_AgentExecutionResultDto()
    {
        var agentNode = CreateNodeDefinition("agent1", "agent", isEntry: true);

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "test",
            CreatedBy = "test",
            Nodes = [agentNode],
            Connections = []
        };

        var callCount = 0;
        var llmClient = new MockLlmClient(_ =>
        {
            callCount++;
            return new LlmResponse
            {
                Content = null,
                ToolCalls =
                [
                    new LlmToolCall
                    {
                        Id = $"call{callCount}",
                        Name = "nonexistent",
                        Arguments = "{}"
                    }
                ]
            };
        });

        var context = CreateContext(workflow: workflow, llmClient: llmClient, currentNodeId: agentNode.Id);
        var node = new AgentNode { MaxIterations = 2 };

        var result = await node.ExecuteAsync(context);

        Assert.False(result.Success);
        Assert.Single(result.Output.Items);

        var data = result.Output.Items[0].Data;
        Assert.NotNull(data);

        var dto = JsonSerializer.Deserialize<AgentExecutionResultDto>(data.ToJsonString(), JsonDefaults.Options);
        Assert.NotNull(dto);
        Assert.Equal("Cancelled", dto.AgentInfo.Status);
        Assert.Equal(0, dto.AgentInfo.IterationCount);
        Assert.Contains("Maximum iterations", dto.AgentInfo.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_LlmError_Output_Deserializes_To_Failed_AgentExecutionResultDto()
    {
        var agentNode = CreateNodeDefinition("agent1", "agent", isEntry: true);

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "test",
            CreatedBy = "test",
            Nodes = [agentNode],
            Connections = []
        };

        var llmClient = new MockLlmClient(_ => throw new InvalidOperationException("API error"));
        var context = CreateContext(workflow: workflow, llmClient: llmClient, currentNodeId: agentNode.Id);
        var node = new AgentNode();

        var result = await node.ExecuteAsync(context);

        Assert.False(result.Success);
        Assert.Single(result.Output.Items);

        var data = result.Output.Items[0].Data;
        Assert.NotNull(data);

        var dto = JsonSerializer.Deserialize<AgentExecutionResultDto>(data.ToJsonString(), JsonDefaults.Options);
        Assert.NotNull(dto);
        Assert.Equal("Failed", dto.AgentInfo.Status);
        // EX-2：客户端侧 DTO 错误不得泄露原始异常文本（如 "API error"），仅保留安全脱敏消息。
        Assert.Equal(NodeErrorFactory.SafeMessage, dto.AgentInfo.ErrorMessage);
        Assert.DoesNotContain("API error", dto.AgentInfo.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ToolCalls_Appear_In_Iterations_Dto()
    {
        var toolNode = CreateNodeDefinition("tool1", "passThrough");
        var agentNode = CreateNodeDefinition("agent1", "agent", isEntry: true);

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "test",
            CreatedBy = "test",
            Nodes = [agentNode, toolNode],
            Connections =
            [
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = toolNode.Id,
                    SourcePortName = FlowConstants.PortNames.Output,
                    TargetNodeId = agentNode.Id,
                    TargetPortName = FlowConstants.PortNames.Tools
                }
            ]
        };

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
            return new LlmResponse { Content = "Final answer" };
        });

        var context = CreateContext(workflow: workflow, llmClient: llmClient, currentNodeId: agentNode.Id);
        var node = new AgentNode();

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        var dto = JsonSerializer.Deserialize<AgentExecutionResultDto>(
            result.Output.Items[0].Data!.ToJsonString(),
            JsonDefaults.Options);
        Assert.NotNull(dto);
        Assert.True(dto.Iterations.Count >= 2);
        var toolCalls = dto.Iterations.SelectMany(i => i.ToolCalls).ToList();
        Assert.NotEmpty(toolCalls);
        Assert.Contains(toolCalls, t => t.ToolName == "tool1" && t.Status == "Completed" && t.Id == "call1");
    }

    [Fact]
    public async Task ExecuteAsync_SubRecords_Are_Empty_By_Default()
    {
        var agentNode = CreateNodeDefinition("agent1", "agent", isEntry: true);
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "test",
            CreatedBy = "test",
            Nodes = [agentNode],
            Connections = []
        };

        var llmClient = new MockLlmClient(_ => new LlmResponse { Content = "Done" });
        var context = CreateContext(workflow: workflow, llmClient: llmClient, currentNodeId: agentNode.Id);
        var node = new AgentNode();

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        var dto = JsonSerializer.Deserialize<AgentExecutionResultDto>(
            result.Output.Items[0].Data!.ToJsonString(),
            JsonDefaults.Options);
        Assert.NotNull(dto);
        Assert.NotNull(dto.SubRecords);
        Assert.Empty(dto.SubRecords);
    }

    [Fact]
    public async Task ExecuteAsync_Streaming_LlmChunks_Are_Populated()
    {
        var agentNode = CreateNodeDefinition("agent1", "agent", isEntry: true);
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "test",
            CreatedBy = "test",
            Nodes = [agentNode],
            Connections = []
        };

        var llmClient = new StreamingMockLlmClient([
            new LlmStreamChunk { Delta = "Hello, ", IsFinal = false },
            new LlmStreamChunk { Delta = "world!", IsFinal = true }
        ]);

        var context = CreateContext(workflow: workflow, llmClient: llmClient, currentNodeId: agentNode.Id);
        var node = new AgentNode();

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        var dto = JsonSerializer.Deserialize<AgentExecutionResultDto>(
            result.Output.Items[0].Data!.ToJsonString(),
            JsonDefaults.Options);
        Assert.NotNull(dto);
        Assert.Single(dto.Iterations);
        Assert.Single(dto.Iterations[0].LlmChunks);
        Assert.Equal("Hello, world!", dto.Iterations[0].LlmChunks[0].Content);
    }

    private NodeExecutionContext CreateContext(
        Workflow? workflow = null,
        ILlmClient? llmClient = null,
        string? currentNodeId = null,
        IReadOnlyDictionary<string, DataBatch>? inputs = null)
    {
        var nodeId = currentNodeId ?? "test-node";
        return new NodeExecutionContext
        {
            Workflow = workflow ?? CreateWorkflow(),
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = nodeId,
                TypeName = "agent",
                Name = "agent1",
                Parameters = []
            },
            Inputs = inputs ?? new Dictionary<string, DataBatch>(),
            RawParameters = new Dictionary<string, object>(),
            ResolvedParameters = new Dictionary<string, object>(),
            Credentials = new TestCredentialAccessor(),
            Logger = NullExecutionLogger.Instance,
            CancellationToken = CancellationToken.None,
            LlmClient = llmClient,
            NodeRegistry = _nodeRegistry
        };
    }

    private static NodeDefinition CreateNodeDefinition(string name, string typeName, bool isEntry = false)
    {
        return new NodeDefinition
        {
            Id = name,
            Name = name,
            TypeName = typeName,
            IsEntry = isEntry,
            Parameters = []
        };
    }

    private static Workflow CreateWorkflow()
    {
        return new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "test",
            CreatedBy = "test",
            Nodes = [],
            Connections = []
        };
    }

    private sealed class MockLlmClient : ILlmClient
    {
        private readonly Func<IReadOnlyList<ToolDefinition>, CancellationToken, Task<LlmResponse>> _responder;

        public string ModelName => "test-model";

        public MockLlmClient(Func<IReadOnlyList<ToolDefinition>, LlmResponse> responder)
        {
            _responder = (tools, _) => Task.FromResult(responder(tools));
        }

        public async Task<LlmResponse> ChatAsync(
            IReadOnlyList<LlmMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            CancellationToken cancellationToken = default)
        {
            return await _responder(tools, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class StreamingMockLlmClient : ILlmClient
    {
        private readonly IReadOnlyList<LlmStreamChunk> _chunks;

        public string ModelName => "test-model";

        public StreamingMockLlmClient(IReadOnlyList<LlmStreamChunk> chunks)
        {
            _chunks = chunks;
        }

        public Task<LlmResponse> ChatAsync(
            IReadOnlyList<LlmMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Use ChatStreamAsync for streaming tests.");
        }

        public async IAsyncEnumerable<LlmStreamChunk> ChatStreamAsync(
            IReadOnlyList<LlmMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var chunk in _chunks)
            {
                await Task.Yield();
                yield return chunk;
            }
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
