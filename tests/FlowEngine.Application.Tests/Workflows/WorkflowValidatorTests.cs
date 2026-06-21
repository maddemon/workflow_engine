using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Application.Tests.Workflows;

public sealed class WorkflowValidatorTests
{
    [Fact]
    public void Validate_ValidWorkflow_ReturnsValid()
    {
        var registry = new StubNodeRegistry([
            CreateDescriptor("start", ports:
            [
                new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
            ]),
            CreateDescriptor("end", ports:
            [
                new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
            ]),
        ]);
        var validator = new WorkflowValidator(registry);
        var workflow = CreateWorkflow([
            new NodeDefinition { Id = Guid.NewGuid(), TypeName = "start", Name = "Start" },
            new NodeDefinition { Id = Guid.NewGuid(), TypeName = "end", Name = "End" },
        ], [
            (nodes) => new Connection
            {
                Id = Guid.NewGuid(),
                SourceNodeId = nodes[0].Id,
                TargetNodeId = nodes[1].Id,
                SourcePortName = "output",
                TargetPortName = "input",
            },
        ]);

        var result = validator.Validate(workflow);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_NullWorkflow_Throws()
    {
        var validator = new WorkflowValidator(new StubNodeRegistry([]));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));
    }

    [Fact]
    public void Validate_DanglingSourceNode_ReportsError()
    {
        var registry = new StubNodeRegistry([]);
        var validator = new WorkflowValidator(registry);
        var workflow = new Workflow
        {
            Nodes = [new NodeDefinition { Id = Guid.NewGuid(), TypeName = "test", Name = "Node" }],
            Connections =
            [
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = Guid.NewGuid(),
                    TargetNodeId = Guid.NewGuid(),
                },
            ],
        };

        var result = validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("源节点不存在"));
        Assert.Contains(result.Errors, e => e.Contains("目标节点不存在"));
    }

    [Fact]
    public void Validate_InvalidSourcePortDirection_ReportsError()
    {
        var registry = new StubNodeRegistry([
            CreateDescriptor("source", ports:
            [
                new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
            ]),
            CreateDescriptor("sink", ports:
            [
                new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
            ]),
        ]);
        var validator = new WorkflowValidator(registry);
        var workflow = CreateWorkflow([
            new NodeDefinition { Id = Guid.NewGuid(), TypeName = "source", Name = "Source" },
            new NodeDefinition { Id = Guid.NewGuid(), TypeName = "sink", Name = "Sink" },
        ], [
            (nodes) => new Connection
            {
                Id = Guid.NewGuid(),
                SourceNodeId = nodes[0].Id,
                TargetNodeId = nodes[1].Id,
                SourcePortName = "input",
                TargetPortName = "input",
            },
        ]);

        var result = validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("不是输出端口"));
    }

    [Fact]
    public void Validate_InvalidTargetPortDirection_ReportsError()
    {
        var registry = new StubNodeRegistry([
            CreateDescriptor("source", ports:
            [
                new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
            ]),
            CreateDescriptor("sink", ports:
            [
                new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
            ]),
        ]);
        var validator = new WorkflowValidator(registry);
        var workflow = CreateWorkflow([
            new NodeDefinition { Id = Guid.NewGuid(), TypeName = "source", Name = "Source" },
            new NodeDefinition { Id = Guid.NewGuid(), TypeName = "sink", Name = "Sink" },
        ], [
            (nodes) => new Connection
            {
                Id = Guid.NewGuid(),
                SourceNodeId = nodes[0].Id,
                TargetNodeId = nodes[1].Id,
                SourcePortName = "output",
                TargetPortName = "output",
            },
        ]);

        var result = validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("不是输入端口"));
    }

    [Fact]
    public void Validate_MissingRequiredParameter_ReportsError()
    {
        var registry = new StubNodeRegistry([
            CreateDescriptor("needsParam", ports:
            [
                new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
            ],
            parameters:
            [
                new ParameterDefinition { Name = "apiKey", DisplayName = "API Key", Required = true },
            ]),
        ]);
        var validator = new WorkflowValidator(registry);
        var workflow = CreateWorkflow([
            new NodeDefinition { Id = Guid.NewGuid(), TypeName = "needsParam", Name = "Needs Key", Parameters = [] },
        ], []);

        var result = validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("缺少必填参数"));
    }

    [Fact]
    public void Validate_CyclicDependency_ReportsError()
    {
        var registry = new StubNodeRegistry([
            CreateDescriptor("node", ports:
            [
                new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
                new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
            ]),
        ]);
        var validator = new WorkflowValidator(registry);
        var nodes = new[]
        {
            new NodeDefinition { Id = Guid.NewGuid(), TypeName = "node", Name = "A" },
            new NodeDefinition { Id = Guid.NewGuid(), TypeName = "node", Name = "B" },
        };
        var workflow = CreateWorkflow(nodes, [
            (n) => new Connection { Id = Guid.NewGuid(), SourceNodeId = n[0].Id, TargetNodeId = n[1].Id, SourcePortName = "output", TargetPortName = "input" },
            (n) => new Connection { Id = Guid.NewGuid(), SourceNodeId = n[1].Id, TargetNodeId = n[0].Id, SourcePortName = "output", TargetPortName = "input" },
        ]);

        var result = validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("循环依赖"));
    }

    private static Workflow CreateWorkflow(
        NodeDefinition[] nodes,
        Func<NodeDefinition[], Connection>[] connectionFactories)
    {
        return new Workflow
        {
            Nodes = nodes.ToList(),
            Connections = connectionFactories.Select(f => f(nodes)).ToList(),
        };
    }

    private static NodeTypeDescriptor CreateDescriptor(
        string typeName,
        List<PortDefinition>? ports = null,
        List<ParameterDefinition>? parameters = null)
    {
        return new NodeTypeDescriptor
        {
            TypeName = typeName,
            DisplayName = typeName,
            Category = "Test",
            Ports = ports ?? [],
            Parameters = parameters ?? [],
        };
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
}
