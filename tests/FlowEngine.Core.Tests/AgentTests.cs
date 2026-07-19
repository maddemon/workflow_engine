using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Agent;
using FlowEngine.Core.Dtos;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Tests;

public class AgentTests
{
    [Fact]
    public void AgentMemory_AddMessage_IncreasesCount()
    {
        var memory = new AgentMemory();
        memory.AddMessage(new LlmMessage { Role = "user", Content = "hello" });

        Assert.Equal(1, memory.Count);
    }

    [Fact]
    public void AgentMemory_AddMessages_Batch_IncreasesCount()
    {
        var memory = new AgentMemory();
        memory.AddMessages([new LlmMessage { Role = "user" }, new LlmMessage { Role = "assistant" }]);

        Assert.Equal(2, memory.Count);
    }

    [Fact]
    public void AgentMemory_WindowSize_TrimsOldMessages()
    {
        var memory = new AgentMemory(2);
        memory.AddMessage(new LlmMessage { Role = "user", Content = "a" });
        memory.AddMessage(new LlmMessage { Role = "user", Content = "b" });
        memory.AddMessage(new LlmMessage { Role = "user", Content = "c" });

        Assert.Equal(2, memory.Count);
        var messages = memory.GetMessages();
        Assert.Equal("b", messages[0].Content);
        Assert.Equal("c", messages[1].Content);
    }

    [Fact]
    public void AgentMemory_Clear_EmptiesMessages()
    {
        var memory = new AgentMemory();
        memory.AddMessage(new LlmMessage { Role = "user" });
        memory.Clear();

        Assert.Equal(0, memory.Count);
    }

    [Fact]
    public void AgentMemory_MergeAndReturnAll_ReturnsCombinedList()
    {
        var memory = new AgentMemory(3);
        memory.AddMessage(new LlmMessage { Role = "user", Content = "a" });
        var combined = memory.MergeAndReturnAll([new LlmMessage { Role = "assistant", Content = "b" }]);

        Assert.Equal(2, combined.Count);
    }

    [Fact]
    public void AgentMemory_WindowSize_MinimumIsOne()
    {
        var memory = new AgentMemory(0);
        memory.AddMessage(new LlmMessage { Role = "user", Content = "a" });
        memory.AddMessage(new LlmMessage { Role = "user", Content = "b" });

        Assert.Equal(1, memory.Count);
    }

    [Fact]
    public void InlineResolverResult_Properties_RoundTrip()
    {
        var result = new InlineResolverResult
        {
            Content = "done",
            Iterations = [new AgentIterationDto { Index = 0 }],
            StoppedReason = InlineResolverStopReason.Completed
        };

        Assert.Equal("done", result.Content);
        Assert.Single(result.Iterations);
        Assert.Equal(InlineResolverStopReason.Completed, result.StoppedReason);
        Assert.Empty(result.ToolExecutionRecords);
    }

    [Fact]
    public void ToolResult_Properties_RoundTrip()
    {
        var result = new ToolResult("tc1", "tool", "input", "output", true, null);

        Assert.Equal("tc1", result.ToolCallId);
        Assert.Equal("tool", result.ToolName);
        Assert.Equal("input", result.Input);
        Assert.Equal("output", result.Output);
        Assert.True(result.Success);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ToolResolution_HasError_WhenErrorIsSet()
    {
        var resolution = new ToolResolution(null, null, null, "error");

        Assert.True(resolution.HasError);
        Assert.Equal("error", resolution.Error);
    }

    [Fact]
    public void ToolResolution_HasError_False_WhenNoError()
    {
        var resolution = new ToolResolution(new ToolDefinition(), new NodeDefinition(), new FakeNodeType(), null);

        Assert.False(resolution.HasError);
    }

    [Fact]
    public void ToolResolver_ToolNotFound_ReturnsError()
    {
        var context = new NodeExecutionContext();
        var resolver = new ToolResolver([], context);

        var result = resolver.Resolve(new LlmToolCall { Name = "missing" });

        Assert.True(result.HasError);
        Assert.Contains("Tool 'missing' not found", result.Error);
    }

    [Fact]
    public void ToolResolver_NodeNotFound_ReturnsError()
    {
        var context = new NodeExecutionContext();
        var tools = new List<ToolDefinition>
        {
            new() { Name = "tool", TargetNodeDefinitionId = "node-1" }
        };
        var resolver = new ToolResolver(tools, context);

        var result = resolver.Resolve(new LlmToolCall { Name = "tool" });

        Assert.True(result.HasError);
        Assert.Contains("node-1", result.Error);
    }

    [Fact]
    public void ToolResolver_NodeTypeNotFound_ReturnsError()
    {
        var workflow = new Workflow();
        workflow.Nodes.Add(new NodeDefinition { Id = "node-1", TypeName = "unknown" });
        var context = new NodeExecutionContext { Workflow = workflow };
        var tools = new List<ToolDefinition>
        {
            new() { Name = "tool", TargetNodeDefinitionId = "node-1" }
        };
        var resolver = new ToolResolver(tools, context);

        var result = resolver.Resolve(new LlmToolCall { Name = "tool" });

        Assert.True(result.HasError);
        Assert.Contains("unknown", result.Error);
    }

    [Fact]
    public void ToolResolver_Resolves_WhenAllFound()
    {
        var workflow = new Workflow();
        workflow.Nodes.Add(new NodeDefinition { Id = "node-1", TypeName = "fake" });
        var context = new NodeExecutionContext
        {
            Workflow = workflow,
            NodeRegistry = new FakeNodeRegistry()
        };
        var tools = new List<ToolDefinition>
        {
            new() { Name = "tool", TargetNodeDefinitionId = "node-1" }
        };
        var resolver = new ToolResolver(tools, context);

        var result = resolver.Resolve(new LlmToolCall { Name = "tool" });

        Assert.False(result.HasError);
        Assert.NotNull(result.Tool);
        Assert.NotNull(result.Node);
        Assert.NotNull(result.NodeType);
    }

    [Fact]
    public void ToolExecutionRecorder_Record_HoldsValues()
    {
        var logger = new FakeExecutionLogger();
        var recorder = new ToolExecutionRecorder(logger);
        var toolNode = new NodeDefinition { Id = "tool", TypeName = "http" };
        var context = new NodeExecutionContext
        {
            Inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase),
            RawParameters = new Dictionary<string, object>(),
            ResolvedParameters = new Dictionary<string, object>()
        };
        var result = new NodeExecutionResult { Success = true };
        var startedAt = DateTime.UtcNow;
        var parentId = Guid.NewGuid();

        var record = recorder.Record(toolNode, context, result, startedAt, parentId);

        Assert.Equal("tool", record.NodeDefinitionId);
        Assert.True(record.Output.Success);
        Assert.Equal(parentId, record.ParentRecordId);
        Assert.True(logger.Messages.Count > 0);
    }

    [Fact]
    public void ToolResultFactory_Error_WithoutInput_CreatesFailedResult()
    {
        var toolCall = new LlmToolCall { Id = "tc1", Name = "tool" };

        var result = ToolResultFactory.Error(toolCall, "failed");

        Assert.False(result.Success);
        Assert.Equal("tc1", result.ToolCallId);
        Assert.Equal("failed", result.Error);
    }

    [Fact]
    public void ToolResultFactory_Error_WithInput_CreatesFailedResult()
    {
        var toolCall = new LlmToolCall { Id = "tc1", Name = "tool" };

        var result = ToolResultFactory.Error(toolCall, "input", "failed");

        Assert.False(result.Success);
        Assert.Equal("input", result.Input);
    }

    [Fact]
    public void ToolResultFactory_Success_CreatesResult()
    {
        var toolCall = new LlmToolCall { Id = "tc1", Name = "tool" };

        var result = ToolResultFactory.Success(toolCall, "input", "output");

        Assert.True(result.Success);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ToolResultFactory_FromExecutionResult_Failed_ReturnsError()
    {
        var toolCall = new LlmToolCall { Id = "tc1", Name = "tool" };
        var nodeResult = new NodeExecutionResult
        {
            Success = false,
            Error = new NodeError { Message = "boom" }
        };

        var result = ToolResultFactory.FromExecutionResult(toolCall, "input", nodeResult);

        Assert.False(result.Success);
        Assert.Contains("boom", result.Error);
    }

    [Fact]
    public void ToolResultFactory_FromExecutionResult_SuccessWithData_ReturnsOutput()
    {
        var toolCall = new LlmToolCall { Id = "tc1", Name = "tool" };
        var nodeResult = new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch
            {
                Items = [new DataItem { Data = JsonValue.Create("ok") }]
            }
        };

        var result = ToolResultFactory.FromExecutionResult(toolCall, "input", nodeResult);

        Assert.True(result.Success);
        Assert.Equal("\"ok\"", result.Output);
    }

    [Fact]
    public void ToolResultFactory_FromExecutionResult_SuccessEmpty_ReturnsDefaultMessage()
    {
        var toolCall = new LlmToolCall { Id = "tc1", Name = "tool" };
        var nodeResult = new NodeExecutionResult { Success = true };

        var result = ToolResultFactory.FromExecutionResult(toolCall, null, nodeResult);

        Assert.True(result.Success);
        Assert.Equal("Tool executed successfully.", result.Output);
    }

    [Fact]
    public async Task ToolContextFactory_CreateAsync_WithoutFactory_CreatesFallbackContext()
    {
        var workflow = new Workflow();
        var toolNode = new NodeDefinition
        {
            Id = "node-1",
            TypeName = "fake",
            Name = "Tool",
            Parameters = new Dictionary<string, object> { ["p"] = 1 }
        };
        var parentContext = new NodeExecutionContext
        {
            Workflow = workflow,
            ExecutionId = Guid.NewGuid(),
            NestingDepth = 1
        };
        var factory = new ToolContextFactory(parentContext, null);
        var resolution = new ToolResolution(null, toolNode, new FakeNodeType(), null);
        var inputBatch = new DataBatch();

        var result = await factory.CreateAsync(resolution, inputBatch, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal("node-1", result.Context.Node.Id);
        Assert.Equal(2, result.Context.NestingDepth);
        Assert.Same(toolNode.Parameters, result.Context.RawParameters);
    }

    private sealed class FakeNodeType : INodeType
    {
        public string TypeName => "fake";
        public string DisplayName => "Fake";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports => [];
        public bool DefaultIsEntry => false;

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult { Success = true });

        public NodeTypeDescriptor GetDescriptor() => new() { TypeName = "fake" };
    }

    private sealed class FakeNodeRegistry : INodeRegistry
    {
        public void Register(INodeType nodeType) { }
        public INodeType Get(string typeName) => new FakeNodeType();
        public bool TryGet(string typeName, out INodeType? nodeType)
        {
            nodeType = new FakeNodeType();
            return true;
        }

        public IReadOnlyCollection<INodeType> GetAll() => [new FakeNodeType()];
        public INodeType CreateInstance(string typeName) => new FakeNodeType();
        public IReadOnlyCollection<NodeTypeDescriptor> GetDescriptors() => [];
        public NodeTypeDescriptor GetDescriptor(string typeName) => new() { TypeName = typeName };
    }

    private sealed class FakeExecutionLogger : IExecutionLogger
    {
        public List<string> Messages { get; } = [];

        public void LogInformation(string message, params object?[] args)
        {
            Messages.Add(args.Length > 0 ? $"{message}: {string.Join(", ", args)}" : message);
        }

        public void LogWarning(string message, params object?[] args)
        {
            Messages.Add(args.Length > 0 ? $"{message}: {string.Join(", ", args)}" : message);
        }

        public void LogError(Exception? exception, string message, params object?[] args)
        {
            Messages.Add(args.Length > 0 ? $"{message}: {string.Join(", ", args)}" : message);
        }
    }
}
