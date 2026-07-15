using System.Text.Json;
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
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Tests.Workflows;

public sealed class ImportExportAuditTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly FlowEngineDbContext _dbContext;
    private readonly CapturingEventBus _eventBus;
    private readonly AuditEventFactory _auditFactory;
    private readonly WorkflowExportService _exportService;

    public ImportExportAuditTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _eventBus = new CapturingEventBus();
        _auditFactory = new AuditEventFactory(new FakeUserContext { UserId = Guid.NewGuid() });
        _exportService = new WorkflowExportService(_dbContext, _eventBus, _auditFactory, new StubAuthorizationGuard());
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task ExportAsync_PublishesExportPerformedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = CreateWorkflow("Export-Me");
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(ct);

        _ = await _exportService.ExportAsync(workflow.Id, "exporter-1", ct);

        var auditEvent = Assert.Single(_eventBus.PublishedEvents);
        Assert.Equal(AuditEventTypes.ExportPerformed, auditEvent.EventType);
        Assert.Equal("Workflow", auditEvent.ResourceType);
        Assert.Equal(workflow.Id, auditEvent.ResourceId);
        Assert.NotNull(auditEvent.Payload);
        Assert.Equal("exporter-1", auditEvent.Payload["exportedBy"]);
    }

    [Fact]
    public async Task ExportBatchAsync_PublishesExportPerformedEventWithCount()
    {
        var ct = TestContext.Current.CancellationToken;
        var w1 = CreateWorkflow("Batch-1");
        var w2 = CreateWorkflow("Batch-2");
        _dbContext.Workflows.AddRange(w1, w2);
        await _dbContext.SaveChangesAsync(ct);

        _ = await _exportService.ExportBatchAsync([w1.Id, w2.Id], "exporter-2", ct);

        var auditEvent = Assert.Single(_eventBus.PublishedEvents);
        Assert.Equal(AuditEventTypes.ExportPerformed, auditEvent.EventType);
        Assert.Equal("Workflow", auditEvent.ResourceType);
        Assert.Equal(Guid.Empty, auditEvent.ResourceId);
        Assert.NotNull(auditEvent.Payload);
        Assert.Equal("exporter-2", auditEvent.Payload["exportedBy"]);
        Assert.Equal(2, auditEvent.Payload["count"]);
    }

    [Fact]
    public async Task ImportAsync_PublishesImportPerformedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = new StubNodeRegistry([
            CreateDescriptor("start", ports:
            [
                new PortDefinition { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
            ]),
        ]);
        var importService = new WorkflowImportService(_dbContext, registry, new WorkflowValidator(registry), _eventBus, _auditFactory, new StubAuthorizationGuard());

        var export = new WorkflowExportResult
        {
            Name = "Imported Workflow",
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "n1",
                    TypeName = "start",
                    Name = "Start",
                    Ports =
                    [
                        new PortInstance { Name = "output", Direction = PortDirection.Output, Type = PortType.Main },
                    ],
                },
            ],
            Connections = [],
        };
        var json = JsonSerializer.Serialize(export, JsonOptions);

        var result = await importService.ImportAsync(json, null, "importer-1", ct);

        Assert.True(result.Success);
        var importEvent = _eventBus.PublishedEvents
            .Single(e => e.EventType == AuditEventTypes.ImportPerformed);
        Assert.Equal("Workflow", importEvent.ResourceType);
        Assert.Equal(result.WorkflowId, importEvent.ResourceId);
        Assert.NotNull(importEvent.Payload);
        Assert.Equal("importer-1", importEvent.Payload["importedBy"]);
        Assert.Equal("Imported Workflow", importEvent.Payload["name"]);
    }

    [Fact]
    public async Task ImportAsync_Failure_DoesNotPublishImportPerformedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = new StubNodeRegistry([]);
        var importService = new WorkflowImportService(_dbContext, registry, new WorkflowValidator(registry), _eventBus, _auditFactory, new StubAuthorizationGuard());

        var export = new WorkflowExportResult
        {
            Name = "Invalid Workflow",
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "n1",
                    TypeName = "unknown",
                    Name = "Bad Node",
                },
            ],
        };
        var json = JsonSerializer.Serialize(export, JsonOptions);

        var result = await importService.ImportAsync(json, null, "importer-1", ct);

        Assert.False(result.Success);
        Assert.DoesNotContain(_eventBus.PublishedEvents, e => e.EventType == AuditEventTypes.ImportPerformed);
    }

    private Workflow CreateWorkflow(string name)
    {
        return new Workflow
        {
            ProjectId = Guid.NewGuid(),
            Name = name,
            CreatedBy = "tester",
            IsActive = true,
            Nodes = [],
            Connections = [],
        };
    }

    private static NodeTypeDescriptor CreateDescriptor(
        string typeName,
        List<PortDefinition>? ports = null)
    {
        return new NodeTypeDescriptor
        {
            TypeName = typeName,
            DisplayName = typeName,
            Category = "Test",
            Ports = ports ?? [],
        };
    }

    private sealed class FakeUserContext : IUserContext
    {
        public bool IsAuthenticated => UserId.HasValue;
        public Guid? UserId { get; set; }
        public string? Email => "test@test.com";
        public IReadOnlyList<string> Roles { get; set; } = [];
    }

    private sealed class CapturingEventBus : IEventBus
    {
        public List<AuditLogEvent> PublishedEvents { get; } = [];

        public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
        {
            if (eventInstance is AuditLogEvent auditEvent)
            {
                PublishedEvents.Add(auditEvent);
            }

            return Task.CompletedTask;
        }

        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
            where TEvent : IDomainEvent => new Disposable();

        private sealed class Disposable : IDisposable
        {
            public void Dispose() { }
        }
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
            descriptors.First(d => d.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class StubAuthorizationGuard : IAuthorizationGuard
    {
        public Task RequireAccessAsync(ResourceKind kind, Guid resourceId, Operation operation, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequireScopeAsync(Scope scope, Operation operation, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequireAdminAsync(Operation operation, CancellationToken ct = default) => Task.CompletedTask;
    }
}
