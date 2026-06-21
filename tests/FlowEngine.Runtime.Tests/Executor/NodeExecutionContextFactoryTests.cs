using System.Text.Json.Nodes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Scripting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Runtime.Tests.Executor;

public sealed class NodeExecutionContextFactoryTests
{
    private readonly NodeExecutionContextFactory _factory;
    private readonly StubNodeRegistry _registry;
    private readonly StubCredentialAccessor _credentialAccessor;

    public NodeExecutionContextFactoryTests()
    {
        _registry = new StubNodeRegistry([
            new NodeTypeDescriptor
            {
                TypeName = "testNode",
                DisplayName = "Test Node",
                Parameters =
                [
                    new ParameterDefinition { Name = "message", DisplayName = "Message", Required = true },
                    new ParameterDefinition { Name = "count", DisplayName = "Count", DefaultValue = 1 },
                ],
                Ports =
                [
                    new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                    new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
                ],
            },
        ]);
        _credentialAccessor = new StubCredentialAccessor();
        _factory = new NodeExecutionContextFactory(
            _registry,
            new ParameterResolver(NullLogger<ParameterResolver>.Instance),
            _credentialAccessor,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateAsync_ValidInputs_ReturnsContext()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "Test" };
        var execution = new ExecutionRecord { Id = Guid.NewGuid(), WorkflowDefinitionId = workflow.Id };
        var node = new NodeDefinition
        {
            Id = Guid.NewGuid(),
            TypeName = "testNode",
            Name = "Node1",
            Parameters = new Dictionary<string, object> { ["message"] = "hello" },
        };
        var nodeInstance = new TestNodeInstance();
        var inputs = new Dictionary<string, DataBatch>();
        var successfulOutputs = new Dictionary<string, DataBatch>();
        var latestBatches = new Dictionary<string, DataBatch>();

        var context = await _factory.CreateAsync(
            workflow, execution, node, nodeInstance,
            inputs, successfulOutputs, latestBatches, 0, ct);

        Assert.NotNull(context);
        Assert.Equal(workflow.Id, context.Workflow.Id);
        Assert.Equal(execution.Id, context.ExecutionId);
        Assert.Equal(node.Id, context.Node.Id);
        Assert.Equal(0, context.RunIndex);
        Assert.Same(inputs, context.Inputs);
    }

    [Fact]
    public async Task CreateAsync_MergesDefaultParameters()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "Test" };
        var execution = new ExecutionRecord { Id = Guid.NewGuid(), WorkflowDefinitionId = workflow.Id };
        var node = new NodeDefinition
        {
            Id = Guid.NewGuid(),
            TypeName = "testNode",
            Name = "Node1",
            Parameters = new Dictionary<string, object> { ["message"] = "hello" },
        };
        var nodeInstance = new TestNodeInstance();

        var context = await _factory.CreateAsync(
            workflow, execution, node, nodeInstance,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            0, ct);

        Assert.NotEmpty(context.RawParameters);
        Assert.True(context.RawParameters.ContainsKey("message"));
        Assert.True(context.RawParameters.ContainsKey("count"));
        Assert.Equal(1, context.RawParameters["count"]);
    }

    [Fact]
    public async Task CreateAsync_WithRunIndex_GetsCurrentInput()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "Test" };
        var execution = new ExecutionRecord { Id = Guid.NewGuid(), WorkflowDefinitionId = workflow.Id };
        var node = new NodeDefinition
        {
            Id = Guid.NewGuid(),
            TypeName = "testNode",
            Name = "Node1",
        };
        var nodeInstance = new TestNodeInstance();
        var inputBatch = new DataBatch
        {
            Items =
            [
                new DataItem { Data = JsonNode.Parse("""{"value": "first"}""") },
                new DataItem { Data = JsonNode.Parse("""{"value": "second"}""") },
            ],
        };
        var inputs = new Dictionary<string, DataBatch> { ["input"] = inputBatch };

        var context = await _factory.CreateAsync(
            workflow, execution, node, nodeInstance,
            inputs,
            new Dictionary<string, DataBatch>(),
            new Dictionary<string, DataBatch>(),
            1, ct);

        Assert.Equal(1, context.RunIndex);
    }

    [Fact]
    public async Task CreateAsync_WithValidEmptyNodeType_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "Test" };
        var execution = new ExecutionRecord { Id = Guid.NewGuid(), WorkflowDefinitionId = workflow.Id };
        var node = new NodeDefinition
        {
            Id = Guid.NewGuid(),
            TypeName = "nonexistent",
            Name = "Bad",
        };
        var instance = new TestNodeInstance();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _factory.CreateAsync(workflow, execution, node, instance,
                new Dictionary<string, DataBatch>(),
                new Dictionary<string, DataBatch>(),
                new Dictionary<string, DataBatch>(),
                0, ct));
    }

    private sealed class TestNodeInstance : INodeType
    {
        public string TypeName => "testNode";
        public string DisplayName => "Test Node";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports =>
        [
            new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
            new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
        ];
        public bool DefaultIsEntry => false;
        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken ct) =>
            Task.FromResult(new NodeExecutionResult { Success = true });
    }

    private sealed class StubNodeRegistry(IReadOnlyCollection<NodeTypeDescriptor> descriptors) : INodeRegistry
    {
        public void Register(INodeType nodeType) { }
        public INodeType Get(string typeName) => throw new InvalidOperationException();
        public bool TryGet(string typeName, out INodeType? nodeType) { nodeType = null; return false; }
        public IReadOnlyCollection<INodeType> GetAll() => [];
        public INodeType CreateInstance(string typeName) => throw new InvalidOperationException();
        public IReadOnlyCollection<NodeTypeDescriptor> GetDescriptors() => descriptors;
        public NodeTypeDescriptor GetDescriptor(string typeName) =>
            descriptors.First(d => d.TypeName == typeName);
    }

    private sealed class StubCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken ct = default) =>
            Task.FromResult(new CredentialValue { Name = "stub", Type = "apiKey" });
    }
}
