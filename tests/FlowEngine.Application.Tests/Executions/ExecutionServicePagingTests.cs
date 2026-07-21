using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Executions;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Tests.TestSupport.Fakes;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.Executor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Application.Tests.Executions;

/// <summary>
/// GetByWorkflowAsync 服务端分页与状态过滤验证（#4）。
/// </summary>
public sealed class ExecutionServicePagingTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly FakeUserContext _userContext;
    private readonly CapturingEngine _engine;
    private readonly RecordingEventBus _eventBus;
    private readonly ExecutionCancellationRegistry _cancellationRegistry;
    private readonly ExecutionService _service;

    public ExecutionServicePagingTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        _userContext = new FakeUserContext();
        _userContext.Roles = [RoleConstants.Admin];
        _engine = new CapturingEngine();
        _eventBus = new RecordingEventBus();
        var auditFactory = new AuditEventFactory(_userContext);
        var resourceAuthorization = new StubResourceAuthorizationService(_userContext);
        var idempotencyService = new StubIdempotencyService();
        _cancellationRegistry = new ExecutionCancellationRegistry();
        _service = new ExecutionService(
            _engine,
            _dbContext,
            idempotencyService,
            AuthorizationGuardFactory.Create(_userContext, resourceAuthorization),
            _eventBus,
            auditFactory,
            _cancellationRegistry);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetByWorkflowAsync_PaginatesAndFiltersByStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "Paging Workflow",
            ProjectId = Guid.NewGuid(),
            Nodes = [],
            Connections = [],
            CreatedBy = "test",
            Version = 1,
            IsActive = true,
        };
        _dbContext.Workflows.Add(workflow);

        // 同一工作流下 20 条 Completed + 5 条 Failed = 25 条。
        var records = new List<ExecutionRecord>();
        for (var i = 0; i < 25; i++)
        {
            var status = i < 20 ? ExecutionStatus.Completed : ExecutionStatus.Failed;
            records.Add(new ExecutionRecord
            {
                Id = Guid.NewGuid(),
                WorkflowDefinitionId = workflow.Id,
                Status = status,
                StartedAt = DateTime.UtcNow.AddMinutes(-i),
                CompletedAt = DateTime.UtcNow.AddMinutes(-i),
                NodeRecords = [],
            });
        }

        _dbContext.ExecutionRecords.AddRange(records);
        await _dbContext.SaveChangesAsync(ct);

        // 第 1 页，每页 20：返回 20 条，总数 25，总页 2。
        var page1 = await _service.GetByWorkflowAsync(workflow.Id, page: 1, pageSize: 20, cancellationToken: ct);
        Assert.Equal(20, page1.Items.Count);
        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(1, page1.Page);
        Assert.Equal(20, page1.PageSize);

        // 第 2 页：返回剩余 5 条。
        var page2 = await _service.GetByWorkflowAsync(workflow.Id, page: 2, pageSize: 20, cancellationToken: ct);
        Assert.Equal(5, page2.Items.Count);
        Assert.Equal(25, page2.TotalCount);

        // 状态过滤：仅 Completed，总数 20 且全部为 Completed。
        var completed = await _service.GetByWorkflowAsync(workflow.Id, status: ExecutionStatus.Completed, cancellationToken: ct);
        Assert.Equal(20, completed.TotalCount);
        Assert.All(completed.Items, s => Assert.Equal("Completed", s.Status));

        // 状态过滤：仅 Failed，总数 5。
        var failed = await _service.GetByWorkflowAsync(workflow.Id, status: ExecutionStatus.Failed, cancellationToken: ct);
        Assert.Equal(5, failed.TotalCount);
    }

    [Fact]
    public async Task GetActiveAsync_ReturnsOnlyPendingOrRunning()
    {
        var ct = TestContext.Current.CancellationToken;
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "Active Workflow",
            ProjectId = Guid.NewGuid(),
            Nodes = [],
            Connections = [],
            CreatedBy = "test",
            Version = 1,
            IsActive = true,
        };
        _dbContext.Workflows.Add(workflow);

        _dbContext.ExecutionRecords.AddRange(new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            Status = ExecutionStatus.Pending,
            StartedAt = DateTime.UtcNow,
            NodeRecords = [],
        }, new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            Status = ExecutionStatus.Running,
            StartedAt = DateTime.UtcNow,
            NodeRecords = [],
        }, new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            Status = ExecutionStatus.Completed,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            NodeRecords = [],
        });
        await _dbContext.SaveChangesAsync(ct);

        var active = await _service.GetActiveAsync(workflow.Id, cancellationToken: ct);
        Assert.Equal(2, active.Count);
        Assert.All(active, s => Assert.NotEqual("Completed", s.Status));
    }

    private sealed class CapturingEngine : IEngine
    {
        public Task<ExecutionId> StartAsync(Guid workflowDefinitionId, object? triggerPayload = null, CancellationToken cancellationToken = default)
            => Task.FromResult(ExecutionId.From(Guid.NewGuid()));

        public Task<ExecutionId> StartAsync(Guid workflowDefinitionId, Workflow preloadedWorkflow, object? triggerPayload = null, CancellationToken cancellationToken = default)
            => StartAsync(workflowDefinitionId, triggerPayload, cancellationToken);
    }

    private sealed class StubIdempotencyService : IExecutionIdempotencyService
    {
        public Task<Guid?> TryGetOrRegisterAsync(string idempotencyKey, Guid executionId, TimeSpan? ttl = null, CancellationToken ct = default)
            => Task.FromResult<Guid?>(null);
        public Task<Guid?> TryGetExistingAsync(string idempotencyKey, CancellationToken ct = default)
            => Task.FromResult<Guid?>(null);
        public Task CleanupExpiredAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class StubResourceAuthorizationService(IUserContext userContext) : IResourceAuthorizationService
    {
        public Task<bool> CanAccessWorkflowAsync(Guid userId, Guid workflowId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(IsAllowed(operation));

        public Task<bool> CanAccessCredentialAsync(Guid userId, Guid credentialId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(IsAllowed(operation));

        public Task<bool> CanAccessExecutionAsync(Guid userId, Guid executionId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(IsAllowed(operation));

        public Task<bool> CanAccessTriggerAsync(Guid userId, Guid triggerId, Operation operation, CancellationToken ct = default)
            => Task.FromResult(IsAllowed(operation));

        public bool ShouldMaskCredentialValues(IReadOnlyList<string> roles) => false;

        private bool IsAllowed(Operation operation)
        {
            var roles = userContext.Roles;
            return operation switch
            {
                Operation.Read => roles.Contains(RoleConstants.Admin) || roles.Contains(RoleConstants.Editor) || roles.Contains(RoleConstants.Viewer),
                Operation.Write => roles.Contains(RoleConstants.Admin) || roles.Contains(RoleConstants.Editor),
                Operation.Delete or Operation.Execute => roles.Contains(RoleConstants.Admin),
                _ => false,
            };
        }
    }
}
