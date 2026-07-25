using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Agent;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Registry;
using FlowEngine.Runtime.Tests.Executor;
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
                new AgentNode(),
                new FailingTestNode(),
                new ThrowingTestNode()
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
        var passThroughNode = CreateNodeDefinition("passthrough1", "passThrough");
        var workflow = CreateWorkflow(passThroughNode);

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
                            Arguments = "{}"
                        }
                    ]
                };
            }
            return new LlmResponse { Content = "Done" };
        });

        var tools = new List<ToolDefinition>
        {
            new() { Name = "tool1", Description = "test", TargetNodeDefinitionId = passThroughNode.Id }
        };

        var parentRecordId = Guid.NewGuid();
        var context = CreateContext(workflow, llmClient);
        var resolver = new InlineResolver(llmClient, tools, context, maxIterations: 3, parentRecordId: parentRecordId);

        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = "Test" }
        };

        var result = await resolver.RunAsync(messages);

        Assert.Equal(InlineResolverStopReason.Completed, result.StoppedReason);
        Assert.NotEmpty(result.ToolExecutionRecords);
        Assert.All(result.ToolExecutionRecords, r => Assert.Equal(parentRecordId, r.ParentRecordId));
    }

    [Fact]
    public async Task RunAsync_Creates_NodeExecutionRecord_With_Null_ParentRecordId()
    {
        var passThroughNode = CreateNodeDefinition("passthrough1", "passThrough");
        var workflow = CreateWorkflow(passThroughNode);

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
                            Arguments = "{}"
                        }
                    ]
                };
            }
            return new LlmResponse { Content = "Done" };
        });

        var tools = new List<ToolDefinition>
        {
            new() { Name = "tool1", Description = "test", TargetNodeDefinitionId = passThroughNode.Id }
        };

        var context = CreateContext(workflow, llmClient);
        var resolver = new InlineResolver(llmClient, tools, context, maxIterations: 3, parentRecordId: null);

        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = "Test" }
        };

        var result = await resolver.RunAsync(messages);

        Assert.Equal(InlineResolverStopReason.Completed, result.StoppedReason);
        Assert.NotEmpty(result.ToolExecutionRecords);
        Assert.All(result.ToolExecutionRecords, r => Assert.Null(r.ParentRecordId));
    }

    [Fact]
    public async Task RunAsync_Executes_Tool_Successfully()
    {
        var passThroughNode = CreateNodeDefinition("passthrough1", "passThrough");
        var workflow = CreateWorkflow(passThroughNode);

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

        var tools = new List<ToolDefinition>
        {
            new() { Name = "tool1", Description = "test", TargetNodeDefinitionId = passThroughNode.Id }
        };

        var context = CreateContext(workflow, llmClient);
        var resolver = new InlineResolver(llmClient, tools, context, maxIterations: 10);

        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = "Test" }
        };

        var result = await resolver.RunAsync(messages);

        Assert.Equal(InlineResolverStopReason.Completed, result.StoppedReason);
        Assert.Equal(2, result.Iterations.Count);
        Assert.Equal("Completed", result.Iterations[0].ToolCalls[0].Status);
    }

    [Fact]
    public async Task RunAsync_Returns_Error_When_Tool_Node_Not_Found()
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
                            Arguments = "{}"
                        }
                    ]
                };
            }
            return new LlmResponse { Content = "Final answer" };
        });

        var tools = new List<ToolDefinition>
        {
            new() { Name = "tool1", Description = "test", TargetNodeDefinitionId = "passthrough1" }
        };

        var context = CreateContext(llmClient: llmClient);
        var resolver = new InlineResolver(llmClient, tools, context, maxIterations: 10);

        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = "Test" }
        };

        var result = await resolver.RunAsync(messages);

        Assert.Equal(InlineResolverStopReason.Completed, result.StoppedReason);
        Assert.Equal("Failed", result.Iterations[0].ToolCalls[0].Status);
        Assert.Contains("not found", result.Iterations[0].ToolCalls[0].Error);
    }

    [Fact]
    public async Task RunAsync_Returns_Error_When_Node_Type_Not_Found()
    {
        var unknownTypeNode = CreateNodeDefinition("unknown1", "nonExistentType");
        var workflow = CreateWorkflow(unknownTypeNode);

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
                            Arguments = "{}"
                        }
                    ]
                };
            }
            return new LlmResponse { Content = "Final answer" };
        });

        var tools = new List<ToolDefinition>
        {
            new() { Name = "tool1", Description = "test", TargetNodeDefinitionId = unknownTypeNode.Id }
        };

        var context = CreateContext(workflow, llmClient);
        var resolver = new InlineResolver(llmClient, tools, context, maxIterations: 10);

        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = "Test" }
        };

        var result = await resolver.RunAsync(messages);

        Assert.Equal(InlineResolverStopReason.Completed, result.StoppedReason);
        Assert.Equal("Failed", result.Iterations[0].ToolCalls[0].Status);
        Assert.Contains("not found", result.Iterations[0].ToolCalls[0].Error);
    }

    [Fact]
    public async Task RunAsync_Returns_Error_When_Tool_Execution_Fails()
    {
        var failingNode = CreateNodeDefinition("failing1", "failingTest");
        var workflow = CreateWorkflow(failingNode);

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
                            Arguments = "{}"
                        }
                    ]
                };
            }
            return new LlmResponse { Content = "Final answer" };
        });

        var tools = new List<ToolDefinition>
        {
            new() { Name = "tool1", Description = "test", TargetNodeDefinitionId = failingNode.Id }
        };

        var context = CreateContext(workflow, llmClient);
        var resolver = new InlineResolver(llmClient, tools, context, maxIterations: 10);

        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = "Test" }
        };

        var result = await resolver.RunAsync(messages);

        Assert.Equal(InlineResolverStopReason.Completed, result.StoppedReason);
        Assert.Equal("Failed", result.Iterations[0].ToolCalls[0].Status);
        Assert.Contains("Tool execution failed", result.Iterations[0].ToolCalls[0].Error);
    }

    [Fact]
    public async Task RunAsync_Returns_Error_When_Tool_Execution_Throws()
    {
        var throwingNode = CreateNodeDefinition("throwing1", "throwingTest");
        var workflow = CreateWorkflow(throwingNode);

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
                            Arguments = "{}"
                        }
                    ]
                };
            }
            return new LlmResponse { Content = "Final answer" };
        });

        var tools = new List<ToolDefinition>
        {
            new() { Name = "tool1", Description = "test", TargetNodeDefinitionId = throwingNode.Id }
        };

        var context = CreateContext(workflow, llmClient);
        var resolver = new InlineResolver(llmClient, tools, context, maxIterations: 10);

        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = "Test" }
        };

        var result = await resolver.RunAsync(messages);

        Assert.Equal(InlineResolverStopReason.Completed, result.StoppedReason);
        Assert.Equal("Failed", result.Iterations[0].ToolCalls[0].Status);
        // EX-2：向 Agent/LLM 暴露的错误不得包含原始异常文本（ThrowingTestNode 抛 "test exception"）。
        var error = result.Iterations[0].ToolCalls[0].Error;
        Assert.DoesNotContain("test exception", error);
        Assert.Equal(NodeErrorFactory.SafeMessage, error);
    }

    [Fact]
    public async Task RunAsync_Handles_Invalid_Tool_Arguments()
    {
        var passThroughNode = CreateNodeDefinition("passthrough1", "passThrough");
        var workflow = CreateWorkflow(passThroughNode);

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
                            Arguments = "invalid json{{"
                        }
                    ]
                };
            }
            return new LlmResponse { Content = "Final answer" };
        });

        var tools = new List<ToolDefinition>
        {
            new() { Name = "tool1", Description = "test", TargetNodeDefinitionId = passThroughNode.Id }
        };

        var context = CreateContext(workflow, llmClient);
        var resolver = new InlineResolver(llmClient, tools, context, maxIterations: 10);

        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = "Test" }
        };

        var result = await resolver.RunAsync(messages);

        Assert.Equal(InlineResolverStopReason.Completed, result.StoppedReason);
    }

    [Fact]
    public async Task RunAsync_ToolExecutionRecord_Populates_All_Fields()
    {
        var passThroughNode = CreateNodeDefinition("passthrough1", "passThrough");
        passThroughNode.Parameters = new Dictionary<string, object>
        {
            ["key"] = "value",
            ["resolvedKey"] = "resolvedValue"
        };
        var workflow = CreateWorkflow(passThroughNode);

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

        var tools = new List<ToolDefinition>
        {
            new() { Name = "tool1", Description = "test", TargetNodeDefinitionId = passThroughNode.Id }
        };

        var parentRecordId = Guid.NewGuid();
        var context = CreateContext(workflow, llmClient);
        var resolver = new InlineResolver(llmClient, tools, context, maxIterations: 10, parentRecordId: parentRecordId);

        var result = await resolver.RunAsync([new LlmMessage { Role = "user", Content = "Test" }]);

        Assert.Equal(InlineResolverStopReason.Completed, result.StoppedReason);
        Assert.Single(result.ToolExecutionRecords);

        var record = result.ToolExecutionRecords[0];
        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.Equal(passThroughNode.Id, record.NodeDefinitionId);
        Assert.Equal(0, record.RunIndex);
        Assert.NotEqual(default, record.StartedAt);
        Assert.NotEqual(default, record.CompletedAt);
        Assert.True(record.Output.Success);
        Assert.Equal(parentRecordId, record.ParentRecordId);
        Assert.Contains("key", record.RawParameters.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("resolvedKey", record.ResolvedParameters.Keys, StringComparer.OrdinalIgnoreCase);
    }

    private NodeExecutionContext CreateContext(
        Workflow? workflow = null,
        ILlmClient? llmClient = null,
        string? currentNodeId = null)
    {
        var nodeId = currentNodeId ?? "test-node";
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
                        SourcePortName = FlowConstants.PortNames.Output,
                        TargetNodeId = nodes[1].Id,
                        TargetPortName = FlowConstants.PortNames.Tools
                    }
                ]
                : []
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
}
