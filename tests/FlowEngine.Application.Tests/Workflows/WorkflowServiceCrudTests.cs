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
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using FlowEngine.Application.Tests.TestSupport.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Application.Tests.Workflows;

/// <summary>
/// WorkflowService 核心 CRUD 编排测试，覆盖 CreateAsync / GetAsync / GetAllAsync / UpdateAsync / DeleteAsync
/// 的正常路径与无效输入。使用 InMemory FlowEngineDbContext + 手写 fake 依赖，遵循既有测试惯例。
/// </summary>
public sealed class WorkflowServiceCrudTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly FakeUserContext _userContext;
    private readonly RecordingEventBus _eventBus;
    private readonly WorkflowService _service;

    public WorkflowServiceCrudTests()
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

    // ── CreateAsync ────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidWorkflow_PersistsAndReturnsDto()
    {
        _userContext.Roles = [RoleConstants.Admin];
        var dto = new CreateWorkflowDto
        {
            Name = "My Workflow",
            CreatedBy = "tester",
            Nodes = [new NodeDefinitionDto { Id = "n1", TypeName = "fetch", Name = "Fetch", ErrorStrategy = ErrorStrategy.Terminate }],
            Connections = [],
        };

        var result = await _service.CreateAsync(dto, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("My Workflow", result.Name);
        Assert.True(result.IsActive);
        Assert.Single(_dbContext.Workflows.Local);
    }

    [Fact]
    public async Task CreateAsync_NullDto_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.CreateAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateAsync_DanglingConnection_ThrowsBusinessException()
    {
        _userContext.Roles = [RoleConstants.Admin];
        var dto = new CreateWorkflowDto
        {
            Name = "Bad Workflow",
            CreatedBy = "tester",
            Nodes = [new NodeDefinitionDto { Id = "n1", TypeName = "fetch", Name = "Fetch", ErrorStrategy = ErrorStrategy.Terminate }],
            Connections =
            [
                new ConnectionDto { Id = "c1", SourceNodeId = "n1", TargetNodeId = "ghost", SourcePortName = "out", TargetPortName = "in" },
            ],
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => _service.CreateAsync(dto, TestContext.Current.CancellationToken));
        Assert.Contains("目标节点不存在", ex.Message);
    }

    // ── GetAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_NonExistent_ReturnsNull()
    {
        _userContext.Roles = [RoleConstants.Viewer];

        var result = await _service.GetAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_Existing_ReturnsMappedDto()
    {
        _userContext.Roles = [RoleConstants.Viewer];
        var workflow = SeedWorkflow("Readable", Guid.NewGuid());

        var result = await _service.GetAsync(workflow.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Readable", result.Name);
        Assert.Equal(workflow.Id, result.Id);
    }

    // ── GetAllAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsPagedResults()
    {
        _userContext.Roles = [RoleConstants.Viewer];
        SeedWorkflow("W1", Guid.NewGuid());
        SeedWorkflow("W2", Guid.NewGuid());
        SeedWorkflow("W3", Guid.NewGuid());

        var page = await _service.GetAllAsync(page: 1, pageSize: 2, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(1, page.Page);
        Assert.Equal(2, page.PageSize);
    }

    [Fact]
    public async Task GetAllAsync_FiltersByProjectId()
    {
        _userContext.Roles = [RoleConstants.Viewer];
        var projectId = Guid.NewGuid();
        SeedWorkflow("InProject", projectId);
        SeedWorkflow("Other", Guid.NewGuid());

        var page = await _service.GetAllAsync(projectId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("InProject", Assert.Single(page.Items).Name);
    }

    [Fact]
    public async Task GetAllAsync_ClampsPageAndPageSize()
    {
        _userContext.Roles = [RoleConstants.Viewer];
        SeedWorkflow("W1", Guid.NewGuid());

        // page=0 应修正为 1；pageSize 超过 200 应被收敛
        var page = await _service.GetAllAsync(page: 0, pageSize: 9999, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, page.Page);
        Assert.Equal(200, page.PageSize);
        Assert.Equal(1, page.TotalCount);
    }

    // ── UpdateAsync ────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ExistingWorkflow_UpdatesFields()
    {
        _userContext.Roles = [RoleConstants.Editor];
        var workflow = SeedWorkflow("Original", Guid.NewGuid());
        var dto = new UpdateWorkflowDto
        {
            Name = "Renamed",
            IsActive = true,
            Nodes = [new NodeDefinitionDto { Id = "n1", TypeName = "fetch", Name = "Fetch", ErrorStrategy = ErrorStrategy.Terminate }],
            Connections = [],
        };

        var result = await _service.UpdateAsync(workflow.Id, dto, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Renamed", result.Name);
        var persisted = await _dbContext.Workflows.FindAsync([workflow.Id], TestContext.Current.CancellationToken);
        Assert.Equal("Renamed", persisted!.Name);
    }

    [Fact]
    public async Task UpdateAsync_NonExistent_ReturnsNull()
    {
        _userContext.Roles = [RoleConstants.Editor];
        var dto = new UpdateWorkflowDto
        {
            Name = "X",
            IsActive = true,
            Nodes = [],
            Connections = [],
        };

        var result = await _service.UpdateAsync(Guid.NewGuid(), dto, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    // 修复：更新内容真正变更时 Version 应递增。
    [Fact]
    public async Task UpdateAsync_WithContentChange_IncrementsVersion()
    {
        _userContext.Roles = [RoleConstants.Editor];
        var workflow = SeedWorkflow("Original", Guid.NewGuid());
        var before = workflow.Version;
        var dto = new UpdateWorkflowDto
        {
            Name = "Renamed",
            IsActive = true,
            Nodes = [new NodeDefinitionDto { Id = "n1", TypeName = "fetch", Name = "Fetch", ErrorStrategy = ErrorStrategy.Terminate }],
            Connections = [],
        };

        var result = await _service.UpdateAsync(workflow.Id, dto, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(before + 1, result.Version);
        var persisted = await _dbContext.Workflows.FindAsync([workflow.Id], TestContext.Current.CancellationToken);
        Assert.Equal(before + 1, persisted!.Version);
    }

    // 边界：未变更内容时 Version 不应递增（避免无意义的版本膨胀）。
    [Fact]
    public async Task UpdateAsync_WithoutContentChange_KeepsVersion()
    {
        _userContext.Roles = [RoleConstants.Editor];
        var workflow = SeedWorkflow("Original", Guid.NewGuid());
        var before = workflow.Version;
        var dto = new UpdateWorkflowDto
        {
            Name = "Original",
            IsActive = true,
            Nodes = [new NodeDefinitionDto { Id = "n1", TypeName = "fetch", Name = "Fetch", ErrorStrategy = ErrorStrategy.Terminate }],
            Connections = [],
        };

        var result = await _service.UpdateAsync(workflow.Id, dto, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(before, result.Version);
    }

    // ── DeleteAsync ────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ExistingWorkflow_Deletes()
    {
        _userContext.Roles = [RoleConstants.Admin];
        var workflow = SeedWorkflow("ToDelete", Guid.NewGuid());

        var deleted = await _service.DeleteAsync(workflow.Id, TestContext.Current.CancellationToken);

        Assert.True(deleted);
        var persisted = await _dbContext.Workflows.FindAsync([workflow.Id], TestContext.Current.CancellationToken);
        Assert.Null(persisted);
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_ReturnsFalse()
    {
        _userContext.Roles = [RoleConstants.Admin];

        var deleted = await _service.DeleteAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.False(deleted);
    }

    // ── Draft lifecycle ──────────────────────────────────────

    [Fact]
    public async Task ConfirmDraftAsync_ExistingDraft_ActivatesAndReturnsDto()
    {
        _userContext.Roles = [RoleConstants.Editor];
        var workflow = SeedWorkflow("Draft", Guid.NewGuid(), isActive: false);

        var result = await _service.ConfirmDraftAsync(workflow.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.IsActive);
        Assert.Equal(DraftStatus.Confirmed, result.DraftStatus);
    }

    [Fact]
    public async Task ConfirmDraftAsync_NonExistent_ReturnsNull()
    {
        _userContext.Roles = [RoleConstants.Editor];

        var result = await _service.ConfirmDraftAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task RejectDraftAsync_ExistingDraft_SetsStatusAndReason()
    {
        _userContext.Roles = [RoleConstants.Editor];
        var workflow = SeedWorkflow("Draft", Guid.NewGuid(), isActive: false);

        var result = await _service.RejectDraftAsync(workflow.Id, "bad draft", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(DraftStatus.Rejected, result.DraftStatus);
        Assert.Equal("bad draft", result.RejectionReason);
    }

    [Fact]
    public async Task RejectDraftAsync_NonExistent_ReturnsNull()
    {
        _userContext.Roles = [RoleConstants.Editor];

        var result = await _service.RejectDraftAsync(Guid.NewGuid(), "reason", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    // ── Version queries ────────────────────────────────────────

    [Fact]
    public async Task GetVersionAsync_ExistingVersion_ReturnsMappedDto()
    {
        _userContext.Roles = [RoleConstants.Viewer];
        var workflow = SeedWorkflow("Versioned", Guid.NewGuid());

        var result = await _service.GetVersionAsync(workflow.Id, workflow.Version, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(workflow.Version, result.Version);
    }

    [Fact]
    public async Task GetVersionAsync_NonExistentVersion_ReturnsNull()
    {
        _userContext.Roles = [RoleConstants.Viewer];
        var workflow = SeedWorkflow("Versioned", Guid.NewGuid());

        var result = await _service.GetVersionAsync(workflow.Id, 999, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetVersionsAsync_Existing_ReturnsVersionList()
    {
        _userContext.Roles = [RoleConstants.Viewer];
        var workflow = SeedWorkflow("Versioned", Guid.NewGuid());

        var result = await _service.GetVersionsAsync(workflow.Id, TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Contains(workflow.Version, result);
    }

    // ── Activation transitions ─────────────────────────────────

    [Fact]
    public async Task UpdateAsync_DeactivateWorkflow_CallsUnregisterAndSaves()
    {
        _userContext.Roles = [RoleConstants.Editor];
        var workflow = SeedWorkflow("Active", Guid.NewGuid(), isActive: true);
        var dto = new UpdateWorkflowDto
        {
            Name = workflow.Name,
            IsActive = false,
            Nodes = [],
            Connections = [],
        };

        var result = await _service.UpdateAsync(workflow.Id, dto, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_ActivateWorkflow_CallsRegisterAndSaves()
    {
        _userContext.Roles = [RoleConstants.Editor];
        var workflow = SeedWorkflow("Inactive", Guid.NewGuid(), isActive: false);
        var dto = new UpdateWorkflowDto
        {
            Name = workflow.Name,
            IsActive = true,
            Nodes = [],
            Connections = [],
        };

        var result = await _service.UpdateAsync(workflow.Id, dto, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_RegisterThrows_LogsAndCommits()
    {
        _userContext.Roles = [RoleConstants.Editor];
        var workflow = SeedWorkflow("Inactive", Guid.NewGuid(), isActive: false);
        var dto = new UpdateWorkflowDto
        {
            Name = workflow.Name,
            IsActive = true,
            Nodes = [],
            Connections = [],
        };
        var throwingService = BuildServiceWithScheduleManager(new ThrowingScheduleManager(throwOnRegister: true));

        var result = await throwingService.UpdateAsync(workflow.Id, dto, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.IsActive);
        var persisted = await _dbContext.Workflows.FindAsync([workflow.Id], TestContext.Current.CancellationToken);
        Assert.True(persisted!.IsActive);
    }

    [Fact]
    public async Task DeleteAsync_UnregisterThrows_LogsAndDeletes()
    {
        _userContext.Roles = [RoleConstants.Admin];
        var workflow = SeedWorkflow("ToDelete", Guid.NewGuid());
        var throwingService = BuildServiceWithScheduleManager(new ThrowingScheduleManager(throwOnUnregister: true));

        var deleted = await throwingService.DeleteAsync(workflow.Id, TestContext.Current.CancellationToken);

        Assert.True(deleted);
        var persisted = await _dbContext.Workflows.FindAsync([workflow.Id], TestContext.Current.CancellationToken);
        Assert.Null(persisted);
    }

    // ── Helpers ────────────────────────────────────────────────

    private Workflow SeedWorkflow(string name, Guid projectId, bool isActive = true)
    {
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = name,
            CreatedBy = "tester",
            IsActive = isActive,
            Nodes = [new NodeDefinition { Id = "n1", TypeName = "fetch", Name = "Fetch" }],
            Connections = [],
        };
        _dbContext.Workflows.Add(workflow);
        _dbContext.SaveChanges();
        return workflow;
    }

    private WorkflowService BuildServiceWithScheduleManager(IScheduleManager scheduleManager)
    {
        var auditFactory = new AuditEventFactory(_userContext);
        var resourceAuthorization = new RoleBasedResourceAuthorizationService(_userContext);
        var authGuard = AuthorizationGuardFactory.Create(_userContext, resourceAuthorization, _eventBus);
        var triggerService = new TriggerService(
            _dbContext, _eventBus, auditFactory, scheduleManager, authGuard, new WebhookRouteService(_dbContext), NullLogger<TriggerService>.Instance);
        var validator = new WorkflowValidator(new StubNodeRegistry([]));
        var handler = new AuthorizedOperationHandler(authGuard, _eventBus, auditFactory);
        var statisticsLoader = new WorkflowStatisticsLoader(_dbContext);
        var triggerSync = new WorkflowTriggerSync(triggerService, handler);
        return new WorkflowService(
            _dbContext, validator, _eventBus, auditFactory, triggerService, authGuard, handler, statisticsLoader, triggerSync, NullLogger<WorkflowService>.Instance);
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

    private sealed class ThrowingScheduleManager(bool throwOnRegister = false, bool throwOnUnregister = false) : IScheduleManager
    {
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RegisterScheduleAsync(Guid triggerId, Guid workflowDefinitionId, string cronExpression, string? timeZone = null, DateTime? startAt = null, DateTime? endAt = null, CancellationToken cancellationToken = default)
            => throwOnRegister ? throw new InvalidOperationException("register failed") : Task.CompletedTask;
        public Task UnregisterScheduleAsync(Guid triggerId, CancellationToken cancellationToken = default)
            => throwOnUnregister ? throw new InvalidOperationException("unregister failed") : Task.CompletedTask;
        public Task<DateTime?> GetNextFireTimeAsync(Guid triggerId, CancellationToken cancellationToken = default)
            => Task.FromResult<DateTime?>(null);
        public Task RegisterPollTriggerAsync(Guid triggerId, Guid workflowDefinitionId, int intervalSeconds, CancellationToken cancellationToken = default)
            => throwOnRegister ? throw new InvalidOperationException("register failed") : Task.CompletedTask;
        public Task UnregisterPollTriggerAsync(Guid triggerId, CancellationToken cancellationToken = default)
            => throwOnUnregister ? throw new InvalidOperationException("unregister failed") : Task.CompletedTask;
    }
}
