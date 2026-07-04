using System.Text.Json.Nodes;
using FlowEngine.Application.Audit;
using FlowEngine.Application.Authorization;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Executions;
using FlowEngine.Application.Identity;
using FlowEngine.Application.Triggers;
using FlowEngine.Application.Workflows;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;
using FlowEngine.Core.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Application.Tests.Integration;

/// <summary>
/// 端到端集成测试（GAP-23）。
/// 覆盖工作流创建 → 查询 → 触发执行 → 查询执行结果的全链路。
/// 使用 InMemoryDatabase + 真实 WorkflowService/ExecutionService + Stub Engine。
/// </summary>
public sealed class WorkflowEndToEndTests : IDisposable
{
    private readonly FlowEngineDbContext _dbContext;
    private readonly WorkflowService _workflowService;
    private readonly ExecutionService _executionService;
    private readonly StubEngine _engine;

    public WorkflowEndToEndTests()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new FlowEngineDbContext(options);
        var eventBus = new InMemoryEventBus();
        var userContext = new FakeUserContext();
        var auditFactory = new AuditEventFactory(userContext);
        var scheduleManager = new FakeScheduleManager();
        var triggerService = new TriggerService(_dbContext, eventBus, auditFactory, scheduleManager, userContext);
        var validator = new WorkflowValidator(new EmptyRegistry());
        _workflowService = new WorkflowService(_dbContext, validator, eventBus, auditFactory, triggerService, userContext);
        _engine = new StubEngine(_dbContext);
        _executionService = new ExecutionService(_engine, _dbContext, _workflowService, new StubIdempotencyService());
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task CreateWorkflow_ThenGet_ReturnsSameNodeStructure()
    {
        // 端到端路径：创建工作流 → 查询工作流 → 验证节点结构与连接关系。
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();

        var dto = new CreateWorkflowDto
        {
            ProjectId = projectId,
            Name = "E2E Workflow",
            CreatedBy = "test-user",
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "start",
                    TypeName = "start",
                    Name = "Start",
                    Ports = [],
                    IsEntry = true,
                    ErrorStrategy = ErrorStrategy.Terminate,
                },
                new NodeDefinitionDto
                {
                    Id = "end",
                    TypeName = "end",
                    Name = "End",
                    Ports = [],
                    ErrorStrategy = ErrorStrategy.Terminate,
                },
            ],
            Connections =
            [
                new ConnectionDto
                {
                    Id = "c_start_end",
                    SourceNodeId = "start",
                    SourcePortName = "output",
                    TargetNodeId = "end",
                    TargetPortName = "input",
                },
            ],
        };

        var created = await _workflowService.CreateAsync(dto, ct);

        Assert.Equal("E2E Workflow", created.Name);
        Assert.Equal(2, created.Nodes.Count);
        Assert.Single(created.Connections);
        Assert.Equal("start", created.Nodes[0].Id);
        Assert.Equal("end", created.Nodes[1].Id);

        var fetched = await _workflowService.GetAsync(created.Id, ct);

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal("E2E Workflow", fetched.Name);
        Assert.Equal(2, fetched.Nodes.Count);
        Assert.Single(fetched.Connections);

        var startNode = fetched.Nodes.First(n => n.Name == "Start");
        Assert.True(startNode.IsEntry);
        Assert.Equal("start", startNode.TypeName);

        var endNode = fetched.Nodes.First(n => n.Name == "End");
        Assert.Equal("end", endNode.TypeName);

        var connection = fetched.Connections[0];
        Assert.Equal(startNode.Id, connection.SourceNodeId);
        Assert.Equal(endNode.Id, connection.TargetNodeId);
        Assert.Equal("output", connection.SourcePortName);
        Assert.Equal("input", connection.TargetPortName);
    }

    [Fact]
    public async Task CreateWorkflow_ThenExecute_ThenQueryExecution_ReturnsCompletedWithNodeRecords()
    {
        // 端到端路径：创建工作流 → 触发执行 → 查询执行状态与结果。
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();

        var dto = new CreateWorkflowDto
        {
            ProjectId = projectId,
            Name = "E2E Exec Workflow",
            CreatedBy = "test-user",
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "start",
                    TypeName = "start",
                    Name = "Start",
                    Ports = [],
                    IsEntry = true,
                    ErrorStrategy = ErrorStrategy.Terminate,
                },
            ],
            Connections = [],
        };

        var workflow = await _workflowService.CreateAsync(dto, ct);

        var execution = await _executionService.ExecuteAsync(workflow.Id, idempotencyKey: null, ct);

        Assert.NotNull(execution);
        Assert.Equal(workflow.Id, execution!.WorkflowDefinitionId);

        var fetched = await _executionService.GetAsync(execution.Id, ct);

        Assert.NotNull(fetched);
        Assert.Equal("Completed", fetched!.Status);
        Assert.Equal(workflow.Id, fetched.WorkflowDefinitionId);
        Assert.NotEmpty(fetched.NodeRecords);

        var nodeRecord = fetched.NodeRecords[0];
        Assert.Equal("Completed", nodeRecord.Status);
        Assert.Equal(0, nodeRecord.RunIndex);
    }

    [Fact]
    public async Task CreateWorkflow_ThenListByWorkflow_ReturnsExecutionSummary()
    {
        // 端到端路径：创建工作流 → 触发执行 → 按工作流查询执行列表。
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();

        var dto = new CreateWorkflowDto
        {
            ProjectId = projectId,
            Name = "E2E List Workflow",
            CreatedBy = "test-user",
            Nodes =
            [
                new NodeDefinitionDto
                {
                    Id = "start",
                    TypeName = "start",
                    Name = "Start",
                    Ports = [],
                    IsEntry = true,
                    ErrorStrategy = ErrorStrategy.Terminate,
                },
            ],
            Connections = [],
        };

        var workflow = await _workflowService.CreateAsync(dto, ct);
        await _executionService.ExecuteAsync(workflow.Id, idempotencyKey: null, ct);

        var executions = await _executionService.GetByWorkflowAsync(workflow.Id, ct);

        Assert.NotEmpty(executions);
        var summary = executions.First();
        Assert.Equal(workflow.Id, summary.WorkflowDefinitionId);
        Assert.Equal("Completed", summary.Status);
    }

    private sealed class StubEngine : IEngine
    {
        private readonly FlowEngineDbContext _dbContext;

        public StubEngine(FlowEngineDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<ExecutionId> StartAsync(
            Guid workflowDefinitionId,
            object? triggerPayload = null,
            CancellationToken cancellationToken = default)
        {
            // 模拟引擎：创建一条已完成的执行记录（含一条节点执行记录）。
            var record = new ExecutionRecord
            {
                WorkflowDefinitionId = workflowDefinitionId,
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
                Status = ExecutionStatus.Completed,
                NodeRecords =
                [
                    new NodeExecutionRecord
                    {
                        NodeDefinitionId = Guid.NewGuid(),
                        RunIndex = 0,
                        StartedAt = DateTime.UtcNow,
                        CompletedAt = DateTime.UtcNow,
                        Output = new NodeExecutionResult
                        {
                            Success = true,
                            Output = new DataBatch
                            {
                                Items =
                                [
                                    new DataItem
                                    {
                                        Data = JsonValue.Create("stub output"),
                                        Success = true,
                                        SourceIndex = 0,
                                    },
                                ],
                            },
                        },
                        Inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase),
                        RawParameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase),
                        ResolvedParameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase),
                    },
                ],
            };

            _dbContext.ExecutionRecords.Add(record);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return ExecutionId.From(record.Id);
        }
    }

    private sealed class EmptyRegistry : INodeRegistry
    {
        public void Register(INodeType nodeType) { }
        public INodeType Get(string typeName) => throw new InvalidOperationException();
        public bool TryGet(string typeName, out INodeType? nodeType) { nodeType = null; return false; }
        public IReadOnlyCollection<INodeType> GetAll() => [];
        public INodeType CreateInstance(string typeName) => throw new InvalidOperationException();
        public IReadOnlyCollection<NodeTypeDescriptor> GetDescriptors() => [];
        public NodeTypeDescriptor GetDescriptor(string typeName) => throw new InvalidOperationException();
    }

    private sealed class FakeUserContext : IUserContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => Guid.NewGuid();
        public string? Email => "test@test.com";
        public IReadOnlyList<string> Roles => [];
    }

    private sealed class InMemoryEventBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken ct = default) where TEvent : IDomainEvent => Task.CompletedTask;
        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : IDomainEvent => new Disposable();
        private sealed class Disposable : IDisposable { public void Dispose() { } }
    }

    private sealed class FakeScheduleManager : IScheduleManager
    {
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RegisterScheduleAsync(Guid triggerId, Guid workflowDefId, string cron, string? tz, DateTime? startAt, DateTime? endAt, CancellationToken ct = default) => Task.CompletedTask;
        public Task UnregisterScheduleAsync(Guid triggerId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<DateTime?> GetNextFireTimeAsync(Guid triggerId, CancellationToken ct = default) => Task.FromResult<DateTime?>(null);
        public Task RegisterPollTriggerAsync(Guid triggerId, Guid workflowDefId, int intervalSeconds, CancellationToken ct = default) => Task.CompletedTask;
        public Task UnregisterPollTriggerAsync(Guid triggerId, CancellationToken ct = default) => Task.CompletedTask;
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
}
