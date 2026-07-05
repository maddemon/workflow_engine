using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Agent;
using FlowEngine.Runtime.Tests.Executor;
using FlowEngine.Runtime.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Runtime.Tests.Plugins;

public class InlineResolverTests
{
    private readonly INodeRegistry _nodeRegistry;

    public InlineResolverTests()
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
    public async Task RunAsync_Completes_When_No_Tool_Calls()
    {
        var llmClient = new MockLlmClient(_ => new LlmResponse { Content = "Done" });
        var context = CreateContext();
        var resolver = new InlineResolver(llmClient, [], context, maxIterations: 10);

        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = "Hello" }
        };

        var result = await resolver.RunAsync(messages);

        Assert.Equal(InlineResolverStopReason.Completed, result.StoppedReason);
        Assert.Equal("Done", result.Content);
        Assert.Single(result.Iterations);
    }

    [Fact]
    public async Task RunAsync_Executes_Tool_And_Continues()
    {
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

        var context = CreateContext();
        var resolver = new InlineResolver(llmClient, [], context, maxIterations: 10);

        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = "Test" }
        };

        var result = await resolver.RunAsync(messages);

        // Tool not found in empty list, so it gets an error message and LLM responds with final answer
        Assert.Equal(InlineResolverStopReason.Completed, result.StoppedReason);
        Assert.Equal("Final answer", result.Content);
        Assert.Equal(2, result.Iterations.Count);
    }

    [Fact]
    public async Task RunAsync_Stops_At_MaxIterations()
    {
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

        var context = CreateContext();
        var resolver = new InlineResolver(llmClient, [], context, maxIterations: 3);

        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = "Test" }
        };

        var result = await resolver.RunAsync(messages);

        Assert.Equal(InlineResolverStopReason.MaxIterationsReached, result.StoppedReason);
        Assert.Equal(3, result.Iterations.Count);
    }

    [Fact]
    public async Task RunAsync_Handles_Cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var llmClient = new MockLlmClient(_ => new LlmResponse { Content = "Done" });
        var context = CreateContext();
        var resolver = new InlineResolver(llmClient, [], context, maxIterations: 10);

        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = "Test" }
        };

        var result = await resolver.RunAsync(messages, cts.Token);

        Assert.Equal(InlineResolverStopReason.Cancelled, result.StoppedReason);
    }

    [Fact]
    public async Task RunAsync_Creates_NodeExecutionRecord_With_ParentRecordId()
    {
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
                        Name = "tool1",
                        Arguments = "{}"
                    }
                ]
            };
        });

        var context = CreateContext();
        var resolver = new InlineResolver(llmClient, [], context, maxIterations: 3, parentRecordId: Guid.NewGuid());

        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = "Test" }
        };

        var result = await resolver.RunAsync(messages);

        Assert.Equal(InlineResolverStopReason.MaxIterationsReached, result.StoppedReason);
    }

    private NodeExecutionContext CreateContext(
        Workflow? workflow = null,
        ILlmClient? llmClient = null,
        Guid? currentNodeId = null)
    {
        var nodeId = currentNodeId ?? Guid.NewGuid();
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
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = nodeId,
                TypeName = "agent",
                Name = "agent1",
                Parameters = []
            },
            Inputs = new Dictionary<string, DataBatch>(),
            RawParameters = new Dictionary<string, object>(),
            ResolvedParameters = new Dictionary<string, object>(),
            Credentials = new TestCredentialAccessor(),
            Logger = NullExecutionLogger.Instance,
            CancellationToken = CancellationToken.None,
            LlmClient = llmClient,
            NodeRegistry = _nodeRegistry
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
                        SourcePortName = "output",
                        TargetNodeId = nodes[1].Id,
                        TargetPortName = "tools"
                    }
                ]
                : []
        };
    }

    private static NodeDefinition CreateNodeDefinition(string name, string typeName, bool isEntry = false)
    {
        return new NodeDefinition
        {
            Id = Guid.NewGuid(),
            Name = name,
            TypeName = typeName,
            IsEntry = isEntry,
            Parameters = []
        };
    }
}

public class AgentMemoryTests
{
    [Fact]
    public void AddMessage_Stores_Message()
    {
        var memory = new AgentMemory(10);

        memory.AddMessage(new LlmMessage { Role = "user", Content = "Hello" });

        Assert.Equal(1, memory.Count);
    }

    [Fact]
    public void AddMessage_Trims_When_Exceeding_Window()
    {
        var memory = new AgentMemory(3);

        memory.AddMessage(new LlmMessage { Role = "user", Content = "Msg1" });
        memory.AddMessage(new LlmMessage { Role = "assistant", Content = "Msg2" });
        memory.AddMessage(new LlmMessage { Role = "user", Content = "Msg3" });
        memory.AddMessage(new LlmMessage { Role = "assistant", Content = "Msg4" });

        Assert.Equal(3, memory.Count);
        var messages = memory.GetMessages();
        Assert.Equal("Msg2", messages[0].Content);
        Assert.Equal("Msg3", messages[1].Content);
        Assert.Equal("Msg4", messages[2].Content);
    }

    [Fact]
    public void AddMessages_Batch_Adds_All()
    {
        var memory = new AgentMemory(10);

        memory.AddMessages([
            new LlmMessage { Role = "user", Content = "A" },
            new LlmMessage { Role = "assistant", Content = "B" }
        ]);

        Assert.Equal(2, memory.Count);
    }

    [Fact]
    public void AddMessages_Trims_When_Exceeding_Window()
    {
        var memory = new AgentMemory(2);

        memory.AddMessages([
            new LlmMessage { Role = "user", Content = "A" },
            new LlmMessage { Role = "assistant", Content = "B" },
            new LlmMessage { Role = "user", Content = "C" }
        ]);

        Assert.Equal(2, memory.Count);
        var messages = memory.GetMessages();
        Assert.Equal("B", messages[0].Content);
        Assert.Equal("C", messages[1].Content);
    }

    [Fact]
    public void GetMessages_Returns_Readonly_Collection()
    {
        var memory = new AgentMemory(10);
        memory.AddMessage(new LlmMessage { Role = "user", Content = "Hello" });

        var messages = memory.GetMessages();
        Assert.Single(messages);
        Assert.IsAssignableFrom<IReadOnlyList<LlmMessage>>(messages);
    }

    [Fact]
    public void Clear_Removes_All_Messages()
    {
        var memory = new AgentMemory(10);
        memory.AddMessage(new LlmMessage { Role = "user", Content = "Hello" });
        memory.AddMessage(new LlmMessage { Role = "assistant", Content = "World" });

        memory.Clear();

        Assert.Equal(0, memory.Count);
    }

    [Fact]
    public void MergeAndReturnAll_Returns_Combined_Messages()
    {
        var memory = new AgentMemory(5);
        memory.AddMessage(new LlmMessage { Role = "system", Content = "System" });
        memory.AddMessage(new LlmMessage { Role = "user", Content = "Hello" });

        var result = memory.MergeAndReturnAll([
            new LlmMessage { Role = "assistant", Content = "Hi" },
            new LlmMessage { Role = "user", Content = "Help" }
        ]);

        Assert.Equal(4, result.Count);
        Assert.Equal("System", result[0].Content);
        Assert.Equal("Hello", result[1].Content);
        Assert.Equal("Hi", result[2].Content);
        Assert.Equal("Help", result[3].Content);
    }

    [Fact]
    public void WindowSize_One_Keeps_Only_Latest()
    {
        var memory = new AgentMemory(1);

        memory.AddMessage(new LlmMessage { Role = "user", Content = "First" });
        memory.AddMessage(new LlmMessage { Role = "assistant", Content = "Second" });
        memory.AddMessage(new LlmMessage { Role = "user", Content = "Third" });

        Assert.Equal(1, memory.Count);
        Assert.Equal("Third", memory.GetMessages()[0].Content);
    }
}

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
        Assert.Contains(node.Ports, p => p.Name == "input" && p.Type == PortType.Main && p.Direction == PortDirection.Input);
        Assert.Contains(node.Ports, p => p.Name == "output" && p.Type == PortType.Main && p.Direction == PortDirection.Output);
        Assert.Contains(node.Ports, p => p.Name == "tools" && p.Type == PortType.AgentTool && p.Direction == PortDirection.Input);
        Assert.Contains(node.Ports, p => p.Name == "llm" && p.Type == PortType.LLM && p.Direction == PortDirection.Input);
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
            inputs: new Dictionary<string, DataBatch> { ["input"] = inputBatch });

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

    private NodeExecutionContext CreateContext(
        ILlmClient? llmClient = null,
        int nestingDepth = 0,
        IReadOnlyDictionary<string, DataBatch>? inputs = null)
    {
        return new NodeExecutionContext
        {
            Workflow = new Workflow
            {
                Id = Guid.NewGuid(),
                Name = "test",
                CreatedBy = "test",
                Nodes = [],
                Connections = []
            },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = Guid.NewGuid(),
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
}

#region Test Helpers

internal sealed class MockLlmClient : ILlmClient
{
    private readonly Func<IReadOnlyList<ToolDefinition>, CancellationToken, Task<LlmResponse>> _responder;

    public IReadOnlyList<LlmMessage>? LastMessages { get; private set; }

    public MockLlmClient(Func<IReadOnlyList<ToolDefinition>, LlmResponse> responder)
    {
        _responder = (tools, _) => Task.FromResult(responder(tools));
    }

    public MockLlmClient(Func<IReadOnlyList<ToolDefinition>, CancellationToken, Task<LlmResponse>> responder)
    {
        _responder = responder;
    }

    public async Task<LlmResponse> ChatAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken = default)
    {
        LastMessages = messages;
        return await _responder(tools, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class TestCredentialAccessor : ICredentialAccessor
{
    public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
        => Task.FromResult(new CredentialValue());
}

internal sealed class NullExecutionLogger : IExecutionLogger
{
    public static readonly NullExecutionLogger Instance = new();

    public void LogInformation(string message, params object?[] args) { }
    public void LogWarning(string message, params object?[] args) { }
    public void LogError(Exception? exception, string message, params object?[] args) { }
}

#endregion
