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
            new NodeDefinition { Id = "start", TypeName = "start", Name = "Start" },
            new NodeDefinition { Id = "end", TypeName = "end", Name = "End" },
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
            Nodes = [new NodeDefinition { Id = "testNode", TypeName = "test", Name = "Node" }],
            Connections =
            [
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = "dangling-source",
                    TargetNodeId = "dangling-target",
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
            new NodeDefinition { Id = "source", TypeName = "source", Name = "Source" },
            new NodeDefinition { Id = "sink", TypeName = "sink", Name = "Sink" },
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
            new NodeDefinition { Id = "source", TypeName = "source", Name = "Source" },
            new NodeDefinition { Id = "sink", TypeName = "sink", Name = "Sink" },
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
            new NodeDefinition { Id = "needsKey", TypeName = "needsParam", Name = "Needs Key", Parameters = [] },
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
            new NodeDefinition { Id = "A", TypeName = "node", Name = "A" },
            new NodeDefinition { Id = "B", TypeName = "node", Name = "B" },
        };
        var workflow = CreateWorkflow(nodes, [
            (n) => new Connection { Id = Guid.NewGuid(), SourceNodeId = n[0].Id, TargetNodeId = n[1].Id, SourcePortName = "output", TargetPortName = "input" },
            (n) => new Connection { Id = Guid.NewGuid(), SourceNodeId = n[1].Id, TargetNodeId = n[0].Id, SourcePortName = "output", TargetPortName = "input" },
        ]);

        var result = validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("循环依赖"));
    }

    [Fact]
    public void Validate_EmptyConnections_NoOrphanError()
    {
        var registry = new StubNodeRegistry([
            CreateDescriptor("node", ports:
            [
                new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
            ]),
        ]);
        var validator = new WorkflowValidator(registry);
        var workflow = CreateWorkflow([
            new NodeDefinition { Id = "n1", TypeName = "node", Name = "N1" },
            new NodeDefinition { Id = "n2", TypeName = "node", Name = "N2" },
        ], []);

        var result = validator.Validate(workflow);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Errors, e => e.Contains("孤立节点"));
    }

    [Fact]
    public void Validate_TriggerNodeWithoutIncoming_NoOrphanError()
    {
        var registry = new StubNodeRegistry([
            CreateDescriptor("trigger", category: "Trigger", ports:
            [
                new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
            ]),
            CreateDescriptor("action", ports:
            [
                new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
            ]),
        ]);
        var validator = new WorkflowValidator(registry);
        var workflow = CreateWorkflow([
            new NodeDefinition { Id = "trigger", TypeName = "trigger", Name = "Trigger" },
            new NodeDefinition { Id = "action", TypeName = "action", Name = "Action" },
        ], [
            (n) => new Connection { Id = Guid.NewGuid(), SourceNodeId = "trigger", TargetNodeId = "action", SourcePortName = "output", TargetPortName = "input" },
        ]);

        var result = validator.Validate(workflow);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Errors, e => e.Contains("孤立节点"));
    }

    [Fact]
    public void Validate_NodeWithOnlyOutgoing_NoOrphanError()
    {
        var registry = new StubNodeRegistry([
            CreateDescriptor("source", ports:
            [
                new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
            ]),
            CreateDescriptor("sink", ports:
            [
                new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
            ]),
        ]);
        var validator = new WorkflowValidator(registry);
        var workflow = CreateWorkflow([
            new NodeDefinition { Id = "source", TypeName = "source", Name = "Source" },
            new NodeDefinition { Id = "sink", TypeName = "sink", Name = "Sink" },
            new NodeDefinition { Id = "orphan", TypeName = "sink", Name = "Orphan" },
        ], [
            (n) => new Connection { Id = Guid.NewGuid(), SourceNodeId = "source", TargetNodeId = "sink", SourcePortName = "output", TargetPortName = "input" },
        ]);

        var result = validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("孤立节点") && e.Contains("Orphan"));
        Assert.DoesNotContain(result.Errors, e => e.Contains("孤立节点") && e.Contains("Source"));
    }

    [Fact]
    public void ValidateTriggerNodes_NoTrigger_ReportsError()
    {
        var registry = new StubNodeRegistry([
            CreateDescriptor("action", ports:
            [
                new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
            ]),
        ]);
        var validator = new WorkflowValidator(registry);
        var workflow = CreateWorkflow([
            new NodeDefinition { Id = "n1", TypeName = "action", Name = "Action" },
        ], []);

        var errors = new List<string>();
        validator.ValidateTriggerNodes(workflow, errors);

        Assert.Contains(errors, e => e.Contains("触发器节点"));
    }

    [Fact]
    public void ValidateTriggerNodes_HasTrigger_NoError()
    {
        var registry = new StubNodeRegistry([
            CreateDescriptor("trigger", category: "Trigger", ports:
            [
                new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
            ]),
        ]);
        var validator = new WorkflowValidator(registry);
        var workflow = CreateWorkflow([
            new NodeDefinition { Id = "t1", TypeName = "trigger", Name = "Trigger" },
        ], []);

        var errors = new List<string>();
        validator.ValidateTriggerNodes(workflow, errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateTriggerNodes_DefaultIsEntry_NoError()
    {
        var registry = new StubNodeRegistry([
            CreateDescriptor("entry", defaultIsEntry: true, ports:
            [
                new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
            ]),
        ]);
        var validator = new WorkflowValidator(registry);
        var workflow = CreateWorkflow([
            new NodeDefinition { Id = "e1", TypeName = "entry", Name = "Entry" },
        ], []);

        var errors = new List<string>();
        validator.ValidateTriggerNodes(workflow, errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_DeriveEntryNodes_SetsFirstTriggerWhenNoneExplicit()
    {
        var registry = new StubNodeRegistry([
            CreateDescriptor("trigger", category: "Trigger", ports:
            [
                new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
            ]),
        ]);
        var validator = new WorkflowValidator(registry);
        var workflow = CreateWorkflow([
            new NodeDefinition { Id = "t1", TypeName = "trigger", Name = "T1", IsEntry = false },
        ], []);

        var result = validator.Validate(workflow);

        Assert.True(result.IsValid);
        Assert.True(workflow.Nodes[0].IsEntry);
    }

    [Fact]
    public void Validate_DeriveEntryNodes_RespectsExplicitIsEntry()
    {
        var registry = new StubNodeRegistry([
            CreateDescriptor("trigger", category: "Trigger", ports:
            [
                new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
            ]),
        ]);
        var validator = new WorkflowValidator(registry);
        var workflow = CreateWorkflow([
            new NodeDefinition { Id = "t1", TypeName = "trigger", Name = "T1", IsEntry = true },
            new NodeDefinition { Id = "t2", TypeName = "trigger", Name = "T2", IsEntry = false },
        ], []);

        validator.Validate(workflow);

        Assert.True(workflow.Nodes[0].IsEntry);
        Assert.False(workflow.Nodes[1].IsEntry);
    }

    [Fact]
    public void Validate_PortTypeMismatch_ReportsError()
    {
        var registry = new StubNodeRegistry([
            CreateDescriptor("source", ports:
            [
                new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.AgentTool },
            ]),
            CreateDescriptor("sink", ports:
            [
                new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Memory },
            ]),
        ]);
        var validator = new WorkflowValidator(registry);
        var workflow = CreateWorkflow([
            new NodeDefinition { Id = "source", TypeName = "source", Name = "Source" },
            new NodeDefinition { Id = "sink", TypeName = "sink", Name = "Sink" },
        ], [
            (n) => new Connection { Id = Guid.NewGuid(), SourceNodeId = n[0].Id, TargetNodeId = n[1].Id, SourcePortName = "output", TargetPortName = "input" },
        ]);

        var result = validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("端口类型不兼容"));
    }

    [Fact]
    public void Validate_ConnectionWithMissingTarget_ReportsError()
    {
        var registry = new StubNodeRegistry([
            CreateDescriptor("source", ports:
            [
                new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
            ]),
        ]);
        var validator = new WorkflowValidator(registry);
        var workflow = CreateWorkflow([
            new NodeDefinition { Id = "source", TypeName = "source", Name = "Source" },
        ], [
            (n) => new Connection { Id = Guid.NewGuid(), SourceNodeId = "source", TargetNodeId = "missing", SourcePortName = "output", TargetPortName = "input" },
        ]);

        var result = validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("目标节点不存在"));
    }

    [Fact]
    public void Validate_UnknownNodeType_IsIgnoredForRequiredParameters()
    {
        var registry = new StubNodeRegistry([
            CreateDescriptor("known", parameters:
            [
                new ParameterDefinition { Name = "required", DisplayName = "Required", Required = true },
            ]),
        ]);
        var validator = new WorkflowValidator(registry);
        var workflow = CreateWorkflow([
            new NodeDefinition { Id = "unknown", TypeName = "unknownType", Name = "Unknown", Parameters = [] },
            new NodeDefinition { Id = "known", TypeName = "known", Name = "Known", Parameters = [] },
        ], []);

        var result = validator.Validate(workflow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Known") && e.Contains("缺少必填参数"));
        Assert.DoesNotContain(result.Errors, e => e.Contains("Unknown"));
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
        string category = "Test",
        List<PortDefinition>? ports = null,
        List<ParameterDefinition>? parameters = null,
        bool defaultIsEntry = false)
    {
        return new NodeTypeDescriptor
        {
            TypeName = typeName,
            DisplayName = typeName,
            Category = category,
            Ports = ports ?? [],
            Parameters = parameters ?? [],
            DefaultIsEntry = defaultIsEntry,
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
