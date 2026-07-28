using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Triggers;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using FlowEngine.Application.Tests.TestSupport.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Application.Tests.Workflows;

/// <summary>
/// WorkflowService.GetVersionInfoAsync 轻量版本查询测试，覆盖正常返回、不存在返回 null、
/// 以及 UpdatedAt 为 null 的旧数据边界。
/// </summary>
public sealed class WorkflowVersionInfoTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly FakeUserContext _userContext;
    private readonly RecordingEventBus _eventBus;
    private readonly WorkflowService _service;

    public WorkflowVersionInfoTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _userContext = new FakeUserContext();
        _eventBus = new RecordingEventBus();
        var auditFactory = new AuditEventFactory(_userContext);
        var scheduleManager = new FakeScheduleManager();
        var resourceAuthorization = new RoleBasedResourceAuthorizationService(_userContext);
        var authGuard = AuthorizationGuardFactory.Create(_userContext, resourceAuthorization, _eventBus);
        var triggerService = new TriggerService(
            _dbContext, _eventBus, auditFactory, scheduleManager, authGuard, new WebhookRouteService(_dbContext), NullLogger<TriggerService>.Instance);
        var validator = new WorkflowValidator(new StubNodeRegistry([]));
        var handler = new AuthorizedOperationHandler(authGuard, _eventBus, auditFactory);
        var statisticsLoader = new WorkflowStatisticsLoader(_dbContext);
        var triggerSync = new WorkflowTriggerSync(triggerService, handler);
        _service = new WorkflowService(
            _dbContext, validator, _eventBus, auditFactory, triggerService, authGuard, handler, statisticsLoader, triggerSync, NullLogger<WorkflowService>.Instance);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task GetVersionInfoAsync_ExistingWorkflow_ReturnsCorrectIdVersionUpdatedAt()
    {
        _userContext.Roles = [RoleConstants.Viewer];
        var workflow = SeedWorkflow("Versioned", Guid.NewGuid(), updatedAt: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await _service.GetVersionInfoAsync(workflow.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(workflow.Id, result.Id);
        Assert.Equal(workflow.Version, result.Version);
        Assert.Equal(workflow.UpdatedAt, result.UpdatedAt);
    }

    [Fact]
    public async Task GetVersionInfoAsync_NonExistent_ReturnsNull()
    {
        _userContext.Roles = [RoleConstants.Viewer];

        var result = await _service.GetVersionInfoAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetVersionInfoAsync_OldDataWithNullUpdatedAt_ReturnsNullUpdatedAt()
    {
        _userContext.Roles = [RoleConstants.Viewer];
        var workflow = SeedWorkflow("Legacy", Guid.NewGuid(), updatedAt: null);

        var result = await _service.GetVersionInfoAsync(workflow.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(workflow.Id, result.Id);
        Assert.Equal(workflow.Version, result.Version);
        Assert.Null(result.UpdatedAt);
    }

    private Workflow SeedWorkflow(string name, Guid projectId, DateTime? updatedAt = null)
    {
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = name,
            CreatedBy = "tester",
            IsActive = true,
            Nodes = [new NodeDefinition { Id = "n1", TypeName = "fetch", Name = "Fetch" }],
            Connections = [],
            UpdatedAt = updatedAt,
        };
        _dbContext.Workflows.Add(workflow);
        _dbContext.SaveChanges();
        return workflow;
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
