using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Tests.Executor;
using FlowEngine.Runtime.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Runtime.Tests.Plugins;

public class AgentNodeTests
{
    private readonly INodeRegistry _nodeRegistry;

    public AgentNodeTests()
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
    public void AgentNode_Has_Correct_TypeName()
    {
        var node = new AgentNode();
        Assert.Equal("agent", node.TypeName);
    }

    [Fact]
    public void AgentNode_Has_Correct_Ports()
    {
        var node = new AgentNode();

        Assert.Equal(4, node.Ports.Count);
        Assert.Contains(node.Ports, p => p.Name == "input" && p.Type == PortType.Main && p.Direction == PortDirection.Input);
        Assert.Contains(node.Ports, p => p.Name == "output" && p.Type == PortType.Main && p.Direction == PortDirection.Output);
        Assert.Contains(node.Ports, p => p.Name == "tools" && p.Type == PortType.AgentTool && p.Direction == PortDirection.Output);
        Assert.Contains(node.Ports, p => p.Name == "llmSupply" && p.Type == PortType.LLMSupply && p.Direction == PortDirection.Input);
    }

    [Fact]
    public void AgentNode_Default_Parameters()
    {
        var node = new AgentNode();
        Assert.Equal(10, node.MaxIterations);
        Assert.Null(node.TimeoutSeconds);
        Assert.Equal(string.Empty, node.PromptTemplate);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Error_When_No_LlmClient()
    {
        var node = new AgentNode();
        var context = CreateContext();

        var result = await node.ExecuteAsync(context);

        Assert.False(result.Success);
        Assert.Equal("MissingLlmClient", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_Calls_LLM_With_No_Tools()
    {
        var node = new AgentNode();
        var llmClient = new MockLlmClient(_ => new LlmResponse { Content = "Done" });
        var workflow = CreateWorkflow();
        var context = CreateContext(workflow: workflow, llmClient: llmClient);

        var result = await node.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.Equal("Done", GetResultContent(result));
    }

    [Fact]
    public async Task ExecuteAsync_Collects_Tools_From_Connections()
    {
        var toolNode = CreateNodeInstance("tool1", "passThrough");
        var agentNode = CreateNodeInstance("agent1", "agent", isEntry: true);

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
                    SourceNodeId = agentNode.Id,
                    SourcePortName = "tools",
                    TargetNodeId = toolNode.Id,
                    TargetPortName = "input"
                }
            ]
        };

        IReadOnlyList<ToolDefinition>? capturedTools = null;
        var llmClient = new MockLlmClient(tools =>
        {
            capturedTools = tools;
            return new LlmResponse { Content = "Done" };
        });

        var context = CreateContext(workflow: workflow, llmClient: llmClient, currentNodeId: agentNode.Id);
        var agent = new AgentNode();

        await agent.ExecuteAsync(context);

        Assert.NotNull(capturedTools);
        Assert.Single(capturedTools);
        Assert.Equal("tool1", capturedTools[0].Name);
        Assert.Equal(toolNode.Id, capturedTools[0].TargetNodeDefinitionId);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Empty_Tools_When_No_Connections()
    {
        var agentNode = CreateNodeInstance("agent1", "agent", isEntry: true);

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "test",
            CreatedBy = "test",
            Nodes = [agentNode],
            Connections = []
        };

        IReadOnlyList<ToolDefinition>? capturedTools = null;
        var llmClient = new MockLlmClient(tools =>
        {
            capturedTools = tools;
            return new LlmResponse { Content = "Done" };
        });

        var context = CreateContext(workflow: workflow, llmClient: llmClient, currentNodeId: agentNode.Id);
        var agent = new AgentNode();

        await agent.ExecuteAsync(context);

        Assert.NotNull(capturedTools);
        Assert.Empty(capturedTools);
    }

    [Fact]
    public async Task ExecuteAsync_Executes_Tool_And_Feeds_Back_To_LLM()
    {
        var toolNode = CreateNodeInstance("tool1", "passThrough");
        var agentNode = CreateNodeInstance("agent1", "agent", isEntry: true);

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
                    SourceNodeId = agentNode.Id,
                    SourcePortName = "tools",
                    TargetNodeId = toolNode.Id,
                    TargetPortName = "input"
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
        var agent = new AgentNode();

        var result = await agent.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.Equal("Final answer", GetResultContent(result));
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_Stops_After_MaxIterations()
    {
        var agentNode = CreateNodeInstance("agent1", "agent", isEntry: true);

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

        var agent = new AgentNode { MaxIterations = 3 };
        var context = CreateContext(workflow: workflow, llmClient: llmClient, currentNodeId: agentNode.Id);

        var result = await agent.ExecuteAsync(context);

        Assert.False(result.Success);
        Assert.Equal("AgentTimeout", result.Error?.Code);
        Assert.Contains("Maximum iterations", result.Error!.Message);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_Handles_LLM_Error()
    {
        var agentNode = CreateNodeInstance("agent1", "agent", isEntry: true);

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
        var agent = new AgentNode();

        var result = await agent.ExecuteAsync(context);

        Assert.False(result.Success);
        Assert.Equal("LlmError", result.Error?.Code);
        Assert.Contains("API error", result.Error!.Message);
    }

    [Fact]
    public async Task ExecuteAsync_Tool_Not_Found_Returns_Error_Message()
    {
        var agentNode = CreateNodeInstance("agent1", "agent", isEntry: true);

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
                            Name = "unknownTool",
                            Arguments = "{}"
                        }
                    ]
                };
            }
            return new LlmResponse { Content = "Done" };
        });

        var context = CreateContext(workflow: workflow, llmClient: llmClient, currentNodeId: agentNode.Id);
        var agent = new AgentNode();

        var result = await agent.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.Equal("Done", GetResultContent(result));
    }

    [Fact]
    public async Task ExecuteAsync_Passes_Input_To_LLM()
    {
        var agentNode = CreateNodeInstance("agent1", "agent", isEntry: true);

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "test",
            CreatedBy = "test",
            Nodes = [agentNode],
            Connections = []
        };

        var llmClient = new MockLlmClient(_ => new LlmResponse { Content = "Done" });

        var inputBatch = new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = JsonNode.Parse("{\"question\": \"What is 2+2?\"}"),
                    Success = true,
                    SourceIndex = 0
                }
            ]
        };

        var context = CreateContext(
            workflow: workflow,
            llmClient: llmClient,
            currentNodeId: agentNode.Id,
            inputs: new Dictionary<string, DataBatch> { ["input"] = inputBatch });

        var agent = new AgentNode();
        await agent.ExecuteAsync(context);

        var messages = llmClient.LastMessages;
        Assert.NotNull(messages);
        Assert.Contains(messages, m => m.Role == "user");
        var userMsg = messages.First(m => m.Role == "user");
        Assert.Contains("question", userMsg.Content);
    }

    [Fact]
    public async Task ExecuteAsync_Uses_PromptTemplate_As_System_Message()
    {
        var agentNode = new AgentNode
        {
            PromptTemplate = "You are a helpful assistant."
        };

        var agentNodeInst = CreateNodeInstance("agent1", "agent", isEntry: true);

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "test",
            CreatedBy = "test",
            Nodes = [agentNodeInst],
            Connections = []
        };

        var llmClient = new MockLlmClient(_ => new LlmResponse { Content = "Done" });
        var context = CreateContext(workflow: workflow, llmClient: llmClient, currentNodeId: agentNodeInst.Id);

        await agentNode.ExecuteAsync(context);

        var messages = llmClient.LastMessages;
        Assert.NotNull(messages);
        Assert.Contains(messages, m => m.Role == "system" && m.Content == "You are a helpful assistant.");
    }

    [Fact]
    public async Task ExecuteAsync_Tool_Result_Fed_Back_To_LLM()
    {
        var toolNode = CreateNodeInstance("tool1", "passThrough");
        var agentNode = CreateNodeInstance("agent1", "agent", isEntry: true);

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
                    SourceNodeId = agentNode.Id,
                    SourcePortName = "tools",
                    TargetNodeId = toolNode.Id,
                    TargetPortName = "input"
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
                            Id = "call_abc",
                            Name = "tool1",
                            Arguments = "{\"data\": \"hello\"}"
                        }
                    ]
                };
            }
            return new LlmResponse { Content = "Result processed" };
        });

        var context = CreateContext(workflow: workflow, llmClient: llmClient, currentNodeId: agentNode.Id);
        var agent = new AgentNode();

        await agent.ExecuteAsync(context);

        var messages = llmClient.LastMessages;
        Assert.NotNull(messages);

        // Should have: system (optional), user, assistant (with tool_calls), tool (result)
        Assert.Contains(messages, m => m.Role == "assistant" && m.ToolCalls is { Count: > 0 });
        Assert.Contains(messages, m => m.Role == "tool" && m.ToolCallId == "call_abc");
        var toolMsg = messages.First(m => m.Role == "tool" && m.ToolCallId == "call_abc");
        Assert.Contains("hello", toolMsg.Content);
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

    private static NodeInstance CreateNodeInstance(
        string name,
        string typeName,
        bool isEntry = false)
    {
        return new NodeInstance
        {
            Id = Guid.NewGuid(),
            Name = name,
            TypeName = typeName,
            IsEntry = isEntry,
            Parameters = []
        };
    }

    private static string? GetResultContent(NodeExecutionResult result)
    {
        if (result.Output.Items.Count == 0)
        {
            return null;
        }

        var data = result.Output.Items[0].Data;
        if (data is null)
        {
            return null;
        }

        if (data is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var str))
        {
            return str;
        }

        return data.ToJsonString();
    }

    private sealed class MockLlmClient : ILlmClient
    {
        private readonly Func<IReadOnlyList<ToolDefinition>, LlmResponse> _responder;

        public IReadOnlyList<LlmMessage>? LastMessages { get; private set; }

        public MockLlmClient(Func<IReadOnlyList<ToolDefinition>, LlmResponse> responder)
        {
            _responder = responder;
        }

        public Task<LlmResponse> ChatAsync(
            IReadOnlyList<LlmMessage> messages,
            IReadOnlyList<ToolDefinition> tools,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages;
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
