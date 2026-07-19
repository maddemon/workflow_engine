#pragma warning disable xUnit1051 // Use TestContext.Current.CancellationToken

using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Tests.Workflows;

/// <summary>
/// WorkflowModificationService 边界与异常路径测试，覆盖 ApplyModify / ApplyAdd / ApplyConnect / ApplyDisconnect 的分支。
/// </summary>
public sealed class WorkflowModificationServiceEdgeTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly StubNodeRegistry _registry;
    private readonly WorkflowValidator _workflowValidator;
    private readonly WorkflowModificationService _service;

    public WorkflowModificationServiceEdgeTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase($"ModEdgeTestDb_{Guid.NewGuid()}")
            .Options;
        _dbContext = new FlowEngineDbContext(options);

        _registry = new StubNodeRegistry([
            TriggerDescriptor,
            HttpDescriptor,
            TransformDescriptor,
            NoOutputDescriptor,
            NoInputDescriptor,
        ]);

        _workflowValidator = new WorkflowValidator(_registry);
        var eventBus = new InMemoryEventBus();
        var auditFactory = new AuditEventFactory(new StubUserContext());
        _service = new WorkflowModificationService(_registry, _dbContext, _workflowValidator, eventBus, auditFactory, new StubAuthorizationGuard());
    }

    public void Dispose() => _dbContext.Dispose();

    private static NodeTypeDescriptor TriggerDescriptor => new()
    {
        TypeName = "webhookTrigger",
        DisplayName = "Webhook Trigger",
        Category = "Trigger",
        DefaultIsEntry = true,
        Ports = [new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }],
    };

    private static NodeTypeDescriptor HttpDescriptor => new()
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
            new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
            new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
        ],
    };

    private static NodeTypeDescriptor TransformDescriptor => new()
    {
        TypeName = "transform",
        DisplayName = "Transform",
        Category = "Data",
        Ports =
        [
            new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main },
            new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
        ],
    };

    private static NodeTypeDescriptor NoOutputDescriptor => new()
    {
        TypeName = "noOutput",
        DisplayName = "No Output",
        Category = "Test",
        Ports = [new PortDefinition { Name = "input", Direction = PortDirection.Input, Type = PortType.Main }],
    };

    private static NodeTypeDescriptor NoInputDescriptor => new()
    {
        TypeName = "noInput",
        DisplayName = "No Input",
        Category = "Test",
        Ports = [new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main }],
    };

    private async Task<Guid> SeedWorkflowAsync()
    {
        var workflow = new Workflow
        {
            Name = "Seed",
            CreatedBy = "test",
            IsActive = true,
            Nodes =
            [
                new NodeDefinition { Id = "trigger", TypeName = "webhookTrigger", Name = "Trigger" },
                new NodeDefinition
                {
                    Id = "fetch", TypeName = "httpRequest", Name = "Fetch",
                    Parameters = new Dictionary<string, object> { ["url"] = "https://api.example.com", ["method"] = "GET" },
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
    public async Task ModifyAsync_NullRequest_ThrowsArgumentNullException()
    {
        var workflowId = await SeedWorkflowAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.ModifyAsync(workflowId, null!));
    }

    [Fact]
    public async Task ModifyAsync_ModifyName_UpdatesName()
    {
        var workflowId = await SeedWorkflowAsync();
        var request = new ModifyWorkflowRequest
        {
            Operations =
            [
                new WorkflowOperation { Op = "modify", Path = "/nodes/fetch/name", Value = "Renamed Fetch" },
            ],
        };

        var result = await _service.ModifyAsync(workflowId, request);

        Assert.Equal("Renamed Fetch", result.Workflow.Nodes.First(n => n.Id == "fetch").Name);
        Assert.Contains(result.Diff, d => d is { Op: "modify", NodeId: "fetch", Field: "name" });
    }

    [Fact]
    public async Task ModifyAsync_ModifyIsEntry_UpdatesFlag()
    {
        var workflowId = await SeedWorkflowAsync();
        var request = new ModifyWorkflowRequest
        {
            Operations =
            [
                new WorkflowOperation { Op = "modify", Path = "/nodes/fetch/isEntry", Value = "true" },
            ],
        };

        var result = await _service.ModifyAsync(workflowId, request);

        Assert.True(result.Workflow.Nodes.First(n => n.Id == "fetch").IsEntry);
    }

    [Fact]
    public async Task ModifyAsync_ModifyDisabled_UpdatesFlag()
    {
        var workflowId = await SeedWorkflowAsync();
        var request = new ModifyWorkflowRequest
        {
            Operations =
            [
                new WorkflowOperation { Op = "modify", Path = "/nodes/fetch/disabled", Value = "true" },
            ],
        };

        var result = await _service.ModifyAsync(workflowId, request);

        Assert.True(result.Workflow.Nodes.First(n => n.Id == "fetch").Disabled);
    }

    [Fact]
    public async Task ModifyAsync_ModifyMissingPath_ThrowsBusinessException()
    {
        var workflowId = await SeedWorkflowAsync();
        var request = new ModifyWorkflowRequest
        {
            Operations = [new WorkflowOperation { Op = "modify", Value = "x" }],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.ModifyAsync(workflowId, request));
        Assert.Contains("Path", ex.Message);
    }

    [Fact]
    public async Task ModifyAsync_AddNode_MissingNode_ThrowsBusinessException()
    {
        var workflowId = await SeedWorkflowAsync();
        var request = new ModifyWorkflowRequest
        {
            Operations = [new WorkflowOperation { Op = "add" }],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.ModifyAsync(workflowId, request));
        Assert.Contains("Node", ex.Message);
    }

    [Fact]
    public async Task ModifyAsync_AddNode_EmptyId_ThrowsBusinessException()
    {
        var workflowId = await SeedWorkflowAsync();
        var request = new ModifyWorkflowRequest
        {
            Operations = [new WorkflowOperation { Op = "add", Node = new AiDraftNodeDto { Id = "", TypeName = "transform" } }],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.ModifyAsync(workflowId, request));
        Assert.Contains("ID", ex.Message);
    }

    [Fact]
    public async Task ModifyAsync_AddNode_DuplicateId_ThrowsBusinessException()
    {
        var workflowId = await SeedWorkflowAsync();
        var request = new ModifyWorkflowRequest
        {
            Operations = [new WorkflowOperation { Op = "add", Node = new AiDraftNodeDto { Id = "fetch", TypeName = "transform" } }],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.ModifyAsync(workflowId, request));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task ModifyAsync_AddNode_MissingTypeName_ThrowsBusinessException()
    {
        var workflowId = await SeedWorkflowAsync();
        var request = new ModifyWorkflowRequest
        {
            Operations = [new WorkflowOperation { Op = "add", Node = new AiDraftNodeDto { Id = "new1", TypeName = "" } }],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.ModifyAsync(workflowId, request));
        Assert.Contains("TypeName", ex.Message);
    }

    [Fact]
    public async Task ModifyAsync_AddNode_UnknownType_ThrowsBusinessException()
    {
        var workflowId = await SeedWorkflowAsync();
        var request = new ModifyWorkflowRequest
        {
            Operations = [new WorkflowOperation { Op = "add", Node = new AiDraftNodeDto { Id = "new1", TypeName = "ghost" } }],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.ModifyAsync(workflowId, request));
        Assert.Contains("unknown node type", ex.Message);
    }

    [Fact]
    public async Task ModifyAsync_AddNode_AfterNodeMissing_ThrowsBusinessException()
    {
        var workflowId = await SeedWorkflowAsync();
        var request = new ModifyWorkflowRequest
        {
            Operations = [new WorkflowOperation { Op = "add", Node = new AiDraftNodeDto { Id = "new1", TypeName = "transform" }, After = "ghost" }],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.ModifyAsync(workflowId, request));
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public async Task ModifyAsync_AddNode_NoMatchingPorts_AddsNodeWithoutConnection()
    {
        var workflowId = await SeedWorkflowAsync();
        var request = new ModifyWorkflowRequest
        {
            Operations = [new WorkflowOperation { Op = "add", Node = new AiDraftNodeDto { Id = "trigger2", TypeName = "webhookTrigger" }, After = "fetch" }],
        };

        var result = await _service.ModifyAsync(workflowId, request);

        Assert.Equal(3, result.Workflow.Nodes.Count);
        Assert.DoesNotContain(result.Workflow.Connections, c => c.TargetNodeId == "trigger2");
    }

    [Fact]
    public async Task ModifyAsync_ConnectNodes_ExplicitPorts_AddsConnection()
    {
        var workflowId = await SeedWorkflowAsync();
        var request = new ModifyWorkflowRequest
        {
            Operations =
            [
                new WorkflowOperation
                {
                    Op = "add",
                    Node = new AiDraftNodeDto { Id = "transform1", TypeName = "transform" },
                },
                new WorkflowOperation
                {
                    Op = "connect",
                    From = "fetch",
                    FromPort = "output",
                    To = "transform1",
                    ToPort = "input",
                },
            ],
        };

        var result = await _service.ModifyAsync(workflowId, request);

        Assert.Contains(result.Workflow.Connections, c =>
            c.SourceNodeId == "fetch" && c.TargetNodeId == "transform1" && c.SourcePortName == "output" && c.TargetPortName == "input");
    }

    [Fact]
    public async Task ModifyAsync_ConnectNodes_InvalidSourcePort_ThrowsBusinessException()
    {
        var workflowId = await SeedWorkflowAsync();
        var request = new ModifyWorkflowRequest
        {
            Operations = [new WorkflowOperation { Op = "connect", From = "fetch", FromPort = "input", To = "trigger" }],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.ModifyAsync(workflowId, request));
        Assert.Contains("does not have output port", ex.Message);
    }

    [Fact]
    public async Task ModifyAsync_ConnectNodes_InvalidTargetPort_ThrowsBusinessException()
    {
        var workflowId = await SeedWorkflowAsync();
        var request = new ModifyWorkflowRequest
        {
            Operations = [new WorkflowOperation { Op = "connect", From = "fetch", To = "fetch", ToPort = "output" }],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.ModifyAsync(workflowId, request));
        Assert.Contains("does not have input port", ex.Message);
    }

    [Fact]
    public async Task ModifyAsync_ConnectNodes_DuplicateConnection_ThrowsBusinessException()
    {
        var workflowId = await SeedWorkflowAsync();
        var request = new ModifyWorkflowRequest
        {
            Operations = [new WorkflowOperation { Op = "connect", From = "trigger", To = "fetch" }],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.ModifyAsync(workflowId, request));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task ModifyAsync_DisconnectNodes_WithPorts_RemovesConnection()
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
                    FromPort = "output",
                    ToPort = "input",
                },
            ],
        };

        var result = await _service.ModifyAsync(workflowId, request);

        Assert.Empty(result.Workflow.Connections);
        Assert.Contains(result.Diff, d => d.Op == "disconnect");
    }

    [Fact]
    public async Task ModifyAsync_DisconnectNodes_MissingConnection_ThrowsBusinessException()
    {
        var workflowId = await SeedWorkflowAsync();
        var request = new ModifyWorkflowRequest
        {
            Operations = [new WorkflowOperation { Op = "disconnect", From = "fetch", To = "trigger" }],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.ModifyAsync(workflowId, request));
        Assert.Contains("No such connection", ex.Message);
    }

    [Fact]
    public async Task ModifyAsync_RemoveTriggerNode_ThrowsValidationException()
    {
        var workflowId = await SeedWorkflowAsync();
        var request = new ModifyWorkflowRequest
        {
            Operations = [new WorkflowOperation { Op = "remove", Path = "/nodes/trigger" }],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.ModifyAsync(workflowId, request));
        Assert.Contains("触发器", ex.Message);
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

    private sealed class StubAuthorizationGuard : IAuthorizationGuard
    {
        public Task RequireAccessAsync(ResourceKind kind, Guid resourceId, Operation operation, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequireScopeAsync(Scope scope, Operation operation, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequireAdminAsync(Operation operation, CancellationToken ct = default) => Task.CompletedTask;
    }
}
