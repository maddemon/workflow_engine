#pragma warning disable xUnit1051 // Use TestContext.Current.CancellationToken

using FlowEngine.Application.Audit;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Tests.Workflows;

public sealed class WorkflowModificationServiceTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly StubNodeRegistry _registry;
    private readonly WorkflowValidator _workflowValidator;
    private readonly WorkflowModificationService _service;

    public WorkflowModificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase($"ModTestDb_{Guid.NewGuid()}")
            .Options;
        _dbContext = new FlowEngineDbContext(options);

        _registry = new StubNodeRegistry(
        [
            new NodeTypeDescriptor
            {
                TypeName = "httpRequest",
                DisplayName = "HTTP Request",
                Category = "HTTP",
                Parameters =
                [
                    new ParameterDefinition { Name = "url", DisplayName = "URL", Type = ParameterType.String, Required = true },
                    new ParameterDefinition { Name = "method", DisplayName = "Method", Type = ParameterType.String, Required = false },
                ],
                Ports =
                [
                    new PortDefinition { Name = "input", DisplayName = "输入", Direction = PortDirection.Input, Type = PortType.Main },
                    new PortDefinition { Name = "output", DisplayName = "输出", Direction = PortDirection.Output, Type = PortType.Main },
                ],
            },
            new NodeTypeDescriptor
            {
                TypeName = "webhookTrigger",
                DisplayName = "Webhook Trigger",
                Category = "Trigger",
                DefaultIsEntry = true,
                Ports =
                [
                    new PortDefinition { Name = "output", DisplayName = "输出", Direction = PortDirection.Output, Type = PortType.Main },
                ],
            },
            new NodeTypeDescriptor
            {
                TypeName = "transform",
                DisplayName = "Transform",
                Category = "Data",
                Ports =
                [
                    new PortDefinition { Name = "input", DisplayName = "输入", Direction = PortDirection.Input, Type = PortType.Main },
                    new PortDefinition { Name = "output", DisplayName = "输出", Direction = PortDirection.Output, Type = PortType.Main },
                ],
            },
        ]);

        _workflowValidator = new WorkflowValidator(_registry);
        var eventBus = new InMemoryEventBus();
        var auditFactory = new AuditEventFactory(new StubUserContext());
        _service = new WorkflowModificationService(_registry, _dbContext, _workflowValidator, eventBus, auditFactory);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private async Task<Guid> SeedWorkflowAsync()
    {
        var workflow = new Workflow
        {
            Name = "Seed Workflow",
            CreatedBy = "test",
            IsActive = true,
            Nodes =
            [
                new NodeDefinition { Id = "trigger", TypeName = "webhookTrigger", Name = "Trigger" },
                new NodeDefinition
                {
                    Id = "fetch", TypeName = "httpRequest", Name = "Fetch",
                    Parameters = new() { ["url"] = "https://api.example.com", ["method"] = "GET" },
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
                    ],
                },
            ],
            Connections =
            [
                new Connection { SourceNodeId = "trigger", SourcePortName = "output", TargetNodeId = "fetch", TargetPortName = "input" },
            ],
        };
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync();
        return workflow.Id;
    }

    private async Task<Guid> SeedWorkflowWithTransformAsync()
    {
        var workflow = new Workflow
        {
            Name = "Seed Workflow With Transform",
            CreatedBy = "test",
            IsActive = true,
            Nodes =
            [
                new NodeDefinition { Id = "trigger", TypeName = "webhookTrigger", Name = "Trigger" },
                new NodeDefinition
                {
                    Id = "fetch", TypeName = "httpRequest", Name = "Fetch",
                    Parameters = new() { ["url"] = "https://api.example.com", ["method"] = "GET" },
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
                    ],
                },
                new NodeDefinition
                {
                    Id = "transform1", TypeName = "transform", Name = "Transform",
                    Ports =
                    [
                        new PortInstance { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
                    ],
                },
            ],
            Connections =
            [
                new Connection { SourceNodeId = "trigger", SourcePortName = "output", TargetNodeId = "fetch", TargetPortName = "input" },
            ],
        };
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync();
        return workflow.Id;
    }

    [Fact]
    public async Task ModifyAsync_ModifyParameter_UpdatesValue()
    {
        var workflowId = await SeedWorkflowAsync();

        var request = new ModifyWorkflowRequest
        {
            Operations =
            [
                new WorkflowOperation
                {
                    Op = "modify",
                    Path = "/nodes/fetch/parameters/url",
                    Value = "https://modified.example.com",
                },
            ],
        };

        var result = await _service.ModifyAsync(workflowId, request);

        Assert.NotEqual(Guid.Empty, result.DraftId);
        Assert.NotEqual(workflowId, result.DraftId);
        Assert.Single(result.Diff);
        Assert.Equal("modify", result.Diff[0].Op);
        Assert.Equal("fetch", result.Diff[0].NodeId);

        var fetchNode = result.Workflow.Nodes.First(n => n.Id == "fetch");
        Assert.Equal("https://modified.example.com", fetchNode.Parameters["url"]);
    }

    [Fact]
    public async Task ModifyAsync_AddNode_CreatesNode()
    {
        var workflowId = await SeedWorkflowAsync();

        var request = new ModifyWorkflowRequest
        {
            Operations =
            [
                new WorkflowOperation
                {
                    Op = "add",
                    Node = new AiDraftNodeDto
                    {
                        Id = "transform1",
                        TypeName = "transform",
                    },
                    After = "fetch",
                },
            ],
        };

        var result = await _service.ModifyAsync(workflowId, request);

        Assert.Equal(3, result.Workflow.Nodes.Count);
        Assert.Contains(result.Workflow.Nodes, n => n.Id == "transform1");
        // Should have created a connection from fetch to transform1
        var conn = result.Workflow.Connections.FirstOrDefault(c => c.TargetNodeId == "transform1");
        Assert.NotNull(conn);
    }

    [Fact]
    public async Task ModifyAsync_RemoveNode_RemovesNodeAndConnections()
    {
        var workflowId = await SeedWorkflowAsync();

        var request = new ModifyWorkflowRequest
        {
            Operations =
            [
                new WorkflowOperation
                {
                    Op = "remove",
                    Path = "/nodes/fetch",
                },
            ],
        };

        var result = await _service.ModifyAsync(workflowId, request);

        Assert.Single(result.Workflow.Nodes); // only trigger remains
        Assert.Empty(result.Workflow.Connections); // connection removed
        Assert.Equal("remove", result.Diff[0].Op);
    }

    [Fact]
    public async Task ModifyAsync_ConnectNodes_AddsConnection()
    {
        var workflowId = await SeedWorkflowWithTransformAsync();

        var request = new ModifyWorkflowRequest
        {
            Operations =
            [
                new WorkflowOperation
                {
                    Op = "connect",
                    From = "trigger",
                    To = "transform1",
                },
            ],
        };

        var result = await _service.ModifyAsync(workflowId, request);

        Assert.Equal(2, result.Workflow.Connections.Count);
        Assert.Contains(result.Diff, d => d.Op == "connect");
    }

    [Fact]
    public async Task ModifyAsync_DisconnectNodes_RemovesConnection()
    {
        var workflowId = await SeedWorkflowAsync();

        var request = new ModifyWorkflowRequest
        {
            Operations =
            [
                new WorkflowOperation
                {
                    Op = "disconnect",
                    From = "trigger",
                    To = "fetch",
                },
            ],
        };

        var result = await _service.ModifyAsync(workflowId, request);

        Assert.Empty(result.Workflow.Connections);
        Assert.Contains(result.Diff, d => d.Op == "disconnect");
    }

    [Fact]
    public async Task ModifyAsync_InvalidPath_ThrowsBusinessException()
    {
        var workflowId = await SeedWorkflowAsync();

        var request = new ModifyWorkflowRequest
        {
            Operations =
            [
                new WorkflowOperation { Op = "modify", Path = "invalid", Value = "test" },
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ModifyAsync(workflowId, request));
        Assert.Contains("路径", ex.Message);
    }

    [Fact]
    public async Task ModifyAsync_NonexistentNode_ThrowsBusinessException()
    {
        var workflowId = await SeedWorkflowAsync();

        var request = new ModifyWorkflowRequest
        {
            Operations =
            [
                new WorkflowOperation { Op = "modify", Path = "/nodes/nonexistent/parameters/url", Value = "test" },
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ModifyAsync(workflowId, request));
        Assert.Contains("不存在", ex.Message);
    }

    [Fact]
    public async Task ModifyAsync_NonexistentWorkflow_ThrowsBusinessException()
    {
        var request = new ModifyWorkflowRequest
        {
            Operations =
            [
                new WorkflowOperation { Op = "modify", Path = "/nodes/fetch/parameters/url", Value = "test" },
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ModifyAsync(Guid.NewGuid(), request));
        Assert.Contains("不存在", ex.Message);
    }

    [Fact]
    public async Task ModifyAsync_InvalidOperationType_ThrowsBusinessException()
    {
        var workflowId = await SeedWorkflowAsync();

        var request = new ModifyWorkflowRequest
        {
            Operations =
            [
                new WorkflowOperation { Op = "invalidOp" },
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _service.ModifyAsync(workflowId, request));
        Assert.Contains("不支持", ex.Message);
    }

    [Fact]
    public async Task ModifyAsync_MultipleOperations_TracksAllDiffs()
    {
        var workflowId = await SeedWorkflowAsync();

        var request = new ModifyWorkflowRequest
        {
            Operations =
            [
                new WorkflowOperation
                {
                    Op = "modify",
                    Path = "/nodes/fetch/parameters/method",
                    Value = "POST",
                },
                new WorkflowOperation
                {
                    Op = "add",
                    Node = new AiDraftNodeDto { Id = "transform1", TypeName = "transform" },
                    After = "fetch",
                },
            ],
        };

        var result = await _service.ModifyAsync(workflowId, request);

        Assert.Equal(3, result.Diff.Count); // modify + add + auto-connect
        Assert.Equal(3, result.Workflow.Nodes.Count);

        var fetchNode = result.Workflow.Nodes.First(n => n.Id == "fetch");
        Assert.Equal("POST", fetchNode.Parameters["method"]);
    }

    // ── Stubs ─────────────────────────────────────────────────

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

    private sealed class InMemoryEventBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent => Task.CompletedTask;
        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
            where TEvent : IDomainEvent => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }

    private sealed class StubUserContext : IUserContext
    {
        public Guid? UserId => Guid.NewGuid();
        public string? Email => "test@example.com";
        public IReadOnlyList<string> Roles => ["admin"];
        public bool IsAuthenticated => true;
    }
}
