using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Agent;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Dtos;
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
        Assert.Contains(node.Ports, p => p.Name == FlowConstants.PortNames.Input && p.Type == PortType.Main && p.Direction == PortDirection.Input);
        Assert.Contains(node.Ports, p => p.Name == FlowConstants.PortNames.Output && p.Type == PortType.Main && p.Direction == PortDirection.Output);
        Assert.Contains(node.Ports, p => p.Name == FlowConstants.PortNames.Tools && p.Type == PortType.AgentTool && p.Direction == PortDirection.Input);
        Assert.Contains(node.Ports, p => p.Name == FlowConstants.PortNames.Llm && p.Type == PortType.LLM && p.Direction == PortDirection.Input);
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
        var agentNode = CreateNodeDefinition("agent1", "agent", isEntry: true);

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
        var agent = new AgentNode();

        var result = await agent.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.Equal("Final answer", GetResultContent(result));
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_Stops_After_MaxIterations()
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
        var agent = new AgentNode();

        var result = await agent.ExecuteAsync(context);

        Assert.False(result.Success);
        Assert.Equal("LlmError", result.Error?.Code);
        Assert.Contains("API error", result.Error!.Message);
    }

    [Fact]
    public async Task ExecuteAsync_Tool_Not_Found_Returns_Error_Message()
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
            inputs: new Dictionary<string, DataBatch> { [FlowConstants.PortNames.Input] = inputBatch });

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

        var agentNodeInst = CreateNodeDefinition("agent1", "agent", isEntry: true);

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

    [Fact]
    public async Task ExecuteAsync_Returns_Timeout_When_LLM_Calls_Timed_Out()
    {
        var agentNode = new AgentNode
        {
            TimeoutSeconds = 1
        };

        var agentNodeInst = CreateNodeDefinition("agent1", "agent", isEntry: true);

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "test",
            CreatedBy = "test",
            Nodes = [agentNodeInst],
            Connections = []
        };

        using var timeoutCts = new CancellationTokenSource();
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var llmClient = new MockLlmClient(async (tools, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new LlmResponse { Content = "Done" };
        });

        var context = CreateContext(
            workflow: workflow,
            llmClient: llmClient,
            currentNodeId: agentNodeInst.Id);

        var result = await agentNode.ExecuteAsync(context, timeoutCts.Token);

        Assert.False(result.Success);
        Assert.Equal("AgentTimeout", result.Error?.Code);
        Assert.Contains("timed out", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_Creates_NodeExecutionRecord_For_Tool_Execution()
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

        var context = CreateContext(
            workflow: workflow,
            llmClient: llmClient,
            currentNodeId: agentNode.Id);

        var agent = new AgentNode();
        var result = await agent.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.Equal("Final answer", GetResultContent(result));
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

    private static NodeDefinition CreateNodeDefinition(
        string name,
        string typeName,
        bool isEntry = false)
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

        var dto = JsonSerializer.Deserialize<AgentExecutionResultDto>(data.ToJsonString(), JsonDefaults.Options);
        if (dto?.Iterations is { Count: > 0 } iterations)
        {
            var lastIteration = iterations[^1];
            if (lastIteration.LlmChunks is { Count: > 0 } chunks)
            {
                return chunks[0].Content;
            }
        }

        return data.ToJsonString();
    }

    [Fact]
    public async Task ExecuteAsync_ToolCalls_Appear_In_Iterations()
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
                        new LlmToolCall { Id = "call1", Name = "tool1", Arguments = "{\"value\": 42}" }
                    ]
                };
            }
            return new LlmResponse { Content = "Done" };
        });

        var context = CreateContext(workflow: workflow, llmClient: llmClient, currentNodeId: agentNode.Id);
        var agent = new AgentNode();

        var result = await agent.ExecuteAsync(context);

        Assert.True(result.Success);
        var dto = GetResultDto(result);
        Assert.NotNull(dto);
        Assert.True(dto.Iterations.Count >= 2);
        var toolCalls = dto.Iterations.SelectMany(i => i.ToolCalls).ToList();
        Assert.NotEmpty(toolCalls);
        Assert.Contains(toolCalls, t => t.ToolName == "tool1" && t.Status == "Completed");
    }

    [Fact]
    public async Task InlineResolver_RunAsync_Collects_ToolExecutionRecords()
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

        var tools = new List<ToolDefinition>
        {
            new ToolDefinition
            {
                Name = "tool1",
                TargetNodeDefinitionId = toolNode.Id,
                ParametersSchema = null
            }
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
                        new LlmToolCall { Id = "call1", Name = "tool1", Arguments = "{\"value\": 42}" }
                    ]
                };
            }
            return new LlmResponse { Content = "Done" };
        });

        var context = CreateContext(workflow: workflow, llmClient: llmClient, currentNodeId: agentNode.Id);
        var resolver = new InlineResolver(llmClient, tools, context, logger: context.Logger);

        var result = await resolver.RunAsync(new List<LlmMessage>(), CancellationToken.None);

        Assert.NotEmpty(result.ToolExecutionRecords);
        var firstRecord = result.ToolExecutionRecords[0];
        Assert.Equal(toolNode.Id, firstRecord.NodeDefinitionId);
        Assert.True(firstRecord.Output.Success);
    }

    [Fact]
    public async Task ExecuteAsync_StreamCallback_Exception_Does_Not_Break_Execution()
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

        var callbackErrorCount = 0;
        var llmClient = new MockLlmClient((tools, ct) => Task.FromResult(new LlmResponse { Content = "Hello" }));

        var context = CreateContext(workflow: workflow, llmClient: llmClient, currentNodeId: agentNode.Id);
        context.OnLlmStreamChunk = (chunk, ct) =>
        {
            callbackErrorCount++;
            throw new InvalidOperationException("stream error");
        };

        var agent = new AgentNode();
        var result = await agent.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.True(callbackErrorCount > 0);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleInputItems_LogsWarning()
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

        var logger = new ListExecutionLogger();
        var llmClient = new MockLlmClient(_ => new LlmResponse { Content = "Hi" });
        var inputs = new Dictionary<string, DataBatch>
        {
            [FlowConstants.PortNames.Input] = new DataBatch
            {
                Items =
                [
                    new DataItem { Data = "first", Success = true, SourceIndex = 0 },
                    new DataItem { Data = "second", Success = true, SourceIndex = 1 }
                ]
            }
        };

        var context = CreateContext(workflow: workflow, llmClient: llmClient, currentNodeId: agentNode.Id, inputs: inputs);
        context.Logger = logger;

        var agent = new AgentNode();
        await agent.ExecuteAsync(context);

        Assert.Contains(logger.Warnings, m => m.Contains("2 条输入数据") || m.Contains("仅处理第一条"));
    }

    [Fact]
    public async Task ExecuteAsync_MemoryEnabled_AddsMessagesToMemory()
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

        var context = CreateContext(workflow: workflow, llmClient: llmClient, currentNodeId: agentNode.Id);
        var agent = new AgentNode { MemoryEnabled = true, MemoryWindowSize = 10 };

        var result = await agent.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.Equal(3, callCount);
        var dto = GetResultDto(result);
        Assert.NotNull(dto);
        Assert.True(dto.Iterations.Count >= 2);
    }

    [Fact]
    public void AgentMemory_AddMessage_And_GetMessages_Works()
    {
        var memory = new FlowEngine.Core.Agent.AgentMemory(5);
        Assert.Equal(0, memory.Count);

        memory.AddMessage(new LlmMessage { Role = "user", Content = "hello" });
        memory.AddMessage(new LlmMessage { Role = "assistant", Content = "hi" });

        Assert.Equal(2, memory.Count);
        var messages = memory.GetMessages();
        Assert.Equal(2, messages.Count);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("hello", messages[0].Content);
    }

    [Fact]
    public void AgentMemory_WindowSize_Trims_Old_Messages()
    {
        var memory = new FlowEngine.Core.Agent.AgentMemory(3);

        for (var i = 0; i < 5; i++)
        {
            memory.AddMessage(new LlmMessage { Role = "user", Content = $"msg-{i}" });
        }

        Assert.Equal(3, memory.Count);
        var messages = memory.GetMessages();
        Assert.Equal("msg-2", messages[0].Content);
        Assert.Equal("msg-4", messages[2].Content);
    }

    private static AgentExecutionResultDto? GetResultDto(NodeExecutionResult result)
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

        return JsonSerializer.Deserialize<AgentExecutionResultDto>(
            data.ToJsonString(),
            JsonDefaults.Options);
    }

    private sealed class ListExecutionLogger : IExecutionLogger
    {
        public List<string> Infos { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> Errors { get; } = new();

        public void LogInformation(string message, params object?[] args)
            => Infos.Add(FormatMessage(message, args));

        public void LogWarning(string message, params object?[] args)
            => Warnings.Add(FormatMessage(message, args));

        public void LogError(Exception? exception, string message, params object?[] args)
            => Errors.Add(FormatMessage(message, args));

        private static string FormatMessage(string message, object?[] args)
        {
            if (args.Length == 0)
            {
                return message;
            }

            try
            {
                return string.Format(message, args);
            }
            catch (FormatException)
            {
                var result = message;
                for (var i = 0; i < args.Length; i++)
                {
                    result = result.Replace($"{{{i}}}", args[i]?.ToString() ?? string.Empty);
                }
                return result;
            }
        }
    }

    private sealed class MockLlmClient : ILlmClient
    {
        private readonly Func<IReadOnlyList<ToolDefinition>, CancellationToken, Task<LlmResponse>> _responder;

        public string ModelName => "test-model";

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
