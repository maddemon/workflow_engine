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

    private NodeExecutionContext CreateContext(
        Workflow? workflow = null,
        ILlmClient? llmClient = null,
        Guid? currentNodeId = null,
        IReadOnlyDictionary<string, DataBatch>? inputs = null)
    {
        var nodeId = currentNodeId ?? Guid.NewGuid();
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
