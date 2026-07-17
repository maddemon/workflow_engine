#pragma warning disable xUnit1051 // Use TestContext.Current.CancellationToken

using FlowEngine.Application.Audit;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Validators;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Application.Authorization;
using FlowEngine.Core.Authorization;
using Scope = FlowEngine.Core.Authorization.Scope;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Application.Tests.Workflows;

public sealed class WorkflowAssemblyServiceTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly StubNodeRegistry _registry;
    private readonly WorkflowService _workflowService;
    private readonly WorkflowValidator _workflowValidator;
    private readonly WorkflowAssemblyService _service;

    public WorkflowAssemblyServiceTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
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
            new NodeTypeDescriptor
            {
                TypeName = "noPort",
                DisplayName = "No Port",
                Category = "Test",
                Ports = [],
            },
        ]);

        _workflowValidator = new WorkflowValidator(_registry);
        _workflowService = CreateWorkflowService();
        _service = new WorkflowAssemblyService(_registry, _workflowService, _workflowValidator);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task AssembleAsync_ValidRequest_CreatesDraftWorkflow()
    {
        var request = new AssembleWorkflowRequest
        {
            Name = "Test Workflow",
            ProjectId = Guid.NewGuid(),
            Nodes =
            [
                new AiDraftNodeDto { Id = "trigger", TypeName = "webhookTrigger" },
                new AiDraftNodeDto { Id = "fetch", TypeName = "httpRequest", Parameters = new() { ["url"] = "https://api.example.com" } },
            ],
            Connections =
            [
                new AiDraftConnectionDto { From = "trigger", To = "fetch" },
            ],
        };

        var result = await _service.AssembleAsync(request);

        Assert.NotEqual(Guid.Empty, result.DraftId);
        Assert.NotNull(result.Workflow);
        Assert.Equal("Test Workflow", result.Workflow.Name);
        Assert.False(result.Workflow.IsActive); // draft must be inactive
        Assert.Equal(2, result.Workflow.Nodes.Count);
        Assert.Single(result.Workflow.Connections);
    }

    [Fact]
    public async Task AssembleAsync_DuplicateNodeIds_ThrowsBusinessException()
    {
        var request = new AssembleWorkflowRequest
        {
            Name = "Dup Test",
            Nodes =
            [
                new AiDraftNodeDto { Id = "dup", TypeName = "httpRequest" },
                new AiDraftNodeDto { Id = "dup", TypeName = "httpRequest" },
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.AssembleAsync(request));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact]
    public async Task AssembleAsync_MissingTypeName_ThrowsBusinessException()
    {
        var request = new AssembleWorkflowRequest
        {
            Name = "No Type",
            Nodes =
            [
                new AiDraftNodeDto { Id = "node1" },
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.AssembleAsync(request));
        Assert.Contains("TypeName", ex.Message);
    }

    [Fact]
    public async Task AssembleAsync_UnknownTypeName_ThrowsBusinessException()
    {
        var request = new AssembleWorkflowRequest
        {
            Name = "Unknown Type",
            Nodes =
            [
                new AiDraftNodeDto { Id = "node1", TypeName = "nonExistentType" },
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.AssembleAsync(request));
        Assert.Contains("unknown", ex.Message);
    }

    [Fact]
    public async Task AssembleAsync_DefaultPortResolution_ConnectsToFirstOutputAndInputPorts()
    {
        var request = new AssembleWorkflowRequest
        {
            Name = "Port Resolution",
            Nodes =
            [
                new AiDraftNodeDto { Id = "trigger", TypeName = "webhookTrigger" },
                new AiDraftNodeDto { Id = "transform", TypeName = "transform" },
            ],
            Connections =
            [
                new AiDraftConnectionDto { From = "trigger", To = "transform" },
            ],
        };

        var result = await _service.AssembleAsync(request);

        var conn = result.Workflow.Connections.Single();
        Assert.Equal("trigger", conn.SourceNodeId);
        Assert.Equal("output", conn.SourcePortName);
        Assert.Equal("transform", conn.TargetNodeId);
        Assert.Equal("input", conn.TargetPortName);
    }

    [Fact]
    public async Task AssembleAsync_ConnectionToNonexistentNode_ThrowsBusinessException()
    {
        var request = new AssembleWorkflowRequest
        {
            Name = "Bad Conn",
            Nodes =
            [
                new AiDraftNodeDto { Id = "trigger", TypeName = "webhookTrigger" },
            ],
            Connections =
            [
                new AiDraftConnectionDto { From = "trigger", To = "nonexistent" },
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.AssembleAsync(request));
        Assert.Contains("non-existent", ex.Message);
    }

    [Fact]
    public async Task AssembleAsync_NoTriggerNode_FailsValidation()
    {
        var request = new AssembleWorkflowRequest
        {
            Name = "No Trigger",
            Nodes =
            [
                new AiDraftNodeDto { Id = "fetch", TypeName = "httpRequest", Parameters = new() { ["url"] = "https://api.example.com" } },
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.AssembleAsync(request));
        Assert.Contains("触发器", ex.Message);
    }

    [Fact]
    public async Task AssembleAsync_MissingRequiredParameter_FailsValidation()
    {
        var request = new AssembleWorkflowRequest
        {
            Name = "Missing Param",
            Nodes =
            [
                new AiDraftNodeDto { Id = "trigger", TypeName = "webhookTrigger" },
                new AiDraftNodeDto { Id = "fetch", TypeName = "httpRequest" },
            ],
            Connections =
            [
                new AiDraftConnectionDto { From = "trigger", To = "fetch" },
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.AssembleAsync(request));
        Assert.Contains("必填参数", ex.Message);
    }

    [Fact]
    public async Task AssembleAsync_NodeIdEmpty_AutoGeneratedFromTypeName()
    {
        // 设计 §5.2 步骤 1：id 缺失时按 typeName 自动生成，并保证唯一。
        var request = new AssembleWorkflowRequest
        {
            Name = "Auto ID",
            Nodes =
            [
                new AiDraftNodeDto { Id = "", TypeName = "webhookTrigger" },
                new AiDraftNodeDto { Id = "fetch", TypeName = "httpRequest", Parameters = new() { ["url"] = "https://api.com" } },
            ],
            Connections =
            [
                new AiDraftConnectionDto { From = "webhookTrigger", To = "fetch" },
            ],
        };

        var result = await _service.AssembleAsync(request);

        var trigger = result.Workflow.Nodes.First(n => n.TypeName == "webhookTrigger");
        Assert.Equal("webhookTrigger", trigger.Id); // 按 typeName 生成
        Assert.Equal("webhookTrigger", result.Workflow.Connections.Single().SourceNodeId); // 连接引用自动生成的 id
    }

    [Fact]
    public async Task AssembleAsync_DuplicateGeneratedIds_GetUniqueSuffix()
    {
        var request = new AssembleWorkflowRequest
        {
            Name = "Auto ID Dup",
            Nodes =
            [
                new AiDraftNodeDto { Id = "", TypeName = "httpRequest", Parameters = new() { ["url"] = "https://a.com" } },
                new AiDraftNodeDto { Id = "", TypeName = "httpRequest", Parameters = new() { ["url"] = "https://b.com" } },
                new AiDraftNodeDto { Id = "trigger", TypeName = "webhookTrigger" },
            ],
            Connections =
            [
                new AiDraftConnectionDto { From = "trigger", To = "httpRequest" },
                new AiDraftConnectionDto { From = "trigger", To = "httpRequest2" },
            ],
        };

        var result = await _service.AssembleAsync(request);

        var httpNodes = result.Workflow.Nodes.Where(n => n.TypeName == "httpRequest").ToList();
        Assert.Equal(2, httpNodes.Count);
        Assert.Contains(httpNodes, n => n.Id == "httpRequest");
        Assert.Contains(httpNodes, n => n.Id == "httpRequest2");
    }

    [Fact]
    public async Task AssembleAsync_AutoLayout_SetsPositionsByDependencyLayer()
    {
        // 设计 §5.2 步骤 6：AI 不填坐标时由后端自动布局。
        var request = new AssembleWorkflowRequest
        {
            Name = "Layout",
            Nodes =
            [
                new AiDraftNodeDto { Id = "trigger", TypeName = "webhookTrigger" },
                new AiDraftNodeDto { Id = "fetch", TypeName = "httpRequest", Parameters = new() { ["url"] = "https://api.com" } },
                new AiDraftNodeDto { Id = "transform", TypeName = "transform" },
            ],
            Connections =
            [
                new AiDraftConnectionDto { From = "trigger", To = "fetch" },
                new AiDraftConnectionDto { From = "fetch", To = "transform" },
            ],
        };

        var result = await _service.AssembleAsync(request);

        var trigger = result.Workflow.Nodes.First(n => n.Id == "trigger");
        var fetch = result.Workflow.Nodes.First(n => n.Id == "fetch");
        var transform = result.Workflow.Nodes.First(n => n.Id == "transform");

        Assert.NotNull(trigger.PositionX);
        Assert.NotNull(fetch.PositionX);
        Assert.NotNull(transform.PositionX);
        // 依赖层级：trigger(层0) < fetch(层1) < transform(层2)
        Assert.True(trigger.PositionX < fetch.PositionX);
        Assert.True(fetch.PositionX < transform.PositionX);
    }

    [Fact]
    public async Task AssembleAsync_FirstTrigger_MarkedAsEntry()
    {
        var request = new AssembleWorkflowRequest
        {
            Name = "Entry",
            Nodes =
            [
                new AiDraftNodeDto { Id = "trigger", TypeName = "webhookTrigger" },
                new AiDraftNodeDto { Id = "fetch", TypeName = "httpRequest", Parameters = new() { ["url"] = "https://api.com" } },
            ],
            Connections =
            [
                new AiDraftConnectionDto { From = "trigger", To = "fetch" },
            ],
        };

        var result = await _service.AssembleAsync(request);

        var trigger = result.Workflow.Nodes.First(n => n.Id == "trigger");
        var fetch = result.Workflow.Nodes.First(n => n.Id == "fetch");
        Assert.True(trigger.IsEntry);
        Assert.False(fetch.IsEntry);
    }

    [Fact]
    public async Task AssembleAsync_MultipleTriggers_AllowsAndMarksFirstAsEntry()
    {
        // 设计 §7.3：允许多个 Trigger，默认取第一个作为入口。
        var request = new AssembleWorkflowRequest
        {
            Name = "Multi Trigger",
            Nodes =
            [
                new AiDraftNodeDto { Id = "t1", TypeName = "webhookTrigger" },
                new AiDraftNodeDto { Id = "t2", TypeName = "webhookTrigger" },
                new AiDraftNodeDto { Id = "fetch", TypeName = "httpRequest", Parameters = new() { ["url"] = "https://api.com" } },
            ],
            Connections =
            [
                new AiDraftConnectionDto { From = "t1", To = "fetch" },
                new AiDraftConnectionDto { From = "t2", To = "fetch" },
            ],
        };

        var result = await _service.AssembleAsync(request);

        var t1 = result.Workflow.Nodes.First(n => n.Id == "t1");
        var t2 = result.Workflow.Nodes.First(n => n.Id == "t2");
        Assert.True(t1.IsEntry);   // 第一个 Trigger 为入口
        Assert.False(t2.IsEntry);  // 其余保持默认
    }

    [Fact]
    public async Task AssembleAsync_ConnectionMissingFrom_ThrowsBusinessException()
    {
        var request = new AssembleWorkflowRequest
        {
            Name = "No From",
            Nodes =
            [
                new AiDraftNodeDto { Id = "trigger", TypeName = "webhookTrigger" },
                new AiDraftNodeDto { Id = "fetch", TypeName = "httpRequest", Parameters = new() { ["url"] = "https://api.com" } },
            ],
            Connections =
            [
                new AiDraftConnectionDto { From = "", To = "fetch" },
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.AssembleAsync(request));
        Assert.Contains("source", ex.Message);
    }

    [Fact]
    public async Task AssembleAsync_NodeWithNoOutputPort_ThrowsBusinessException()
    {
        var request = new AssembleWorkflowRequest
        {
            Name = "No Output Port",
            Nodes =
            [
                new AiDraftNodeDto { Id = "noPort", TypeName = "noPort" },
                new AiDraftNodeDto { Id = "fetch", TypeName = "httpRequest", Parameters = new() { ["url"] = "https://api.com" } },
            ],
            Connections =
            [
                new AiDraftConnectionDto { From = "noPort", To = "fetch" },
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.AssembleAsync(request));
        Assert.Contains("output port", ex.Message);
    }

    private WorkflowService CreateWorkflowService()
    {
        return new WorkflowService(
            _dbContext,
            _workflowValidator,
            new InMemoryEventBus(),
            new AuditEventFactory(new StubUserContext()),
            null!, // TriggerService - not used in CreateDraftAsync
            new StubCoreAuthorizationGuard(),
            null!, // AuthorizedOperationHandler - not used in CreateDraftAsync
            null!, // WorkflowStatisticsLoader - not used in CreateDraftAsync
            null!, // WorkflowTriggerSync - not used in CreateDraftAsync
            NullLogger<WorkflowService>.Instance,
            new CreateWorkflowDtoValidator(),
            new UpdateWorkflowDtoValidator()
        );
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

    private sealed class StubCoreAuthorizationGuard : global::FlowEngine.Application.Authorization.IAuthorizationGuard
    {
        public Task RequireAccessAsync(ResourceKind kind, Guid resourceId, Operation operation, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequireScopeAsync(Scope scope, Operation operation, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequireAdminAsync(Operation operation, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubUserContext : IUserContext
    {
        public Guid? UserId => Guid.NewGuid();
        public string? Email => "test@example.com";
        public IReadOnlyList<string> Roles => ["admin"];
        public bool IsAuthenticated => true;
    }
}
