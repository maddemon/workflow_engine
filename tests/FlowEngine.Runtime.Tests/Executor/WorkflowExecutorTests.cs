using System.Reflection;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Events;
using FlowEngine.Core.Enums;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Registry;
using FlowEngine.Core.Scripting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlowEngine.Runtime.Tests.Executor;

public class WorkflowExecutorTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly FlowEngineDbContext _dbContext;
    private readonly INodeRegistry _nodeRegistry;
    private readonly WorkflowExecutor _executor;
    private readonly WorkflowExecutionQueue _executionQueue;
    private readonly RecordingEventBus _eventBus = new();

    public WorkflowExecutorTests()
    {
        _dbContext = CreateDbContext();

        _nodeRegistry = new NodeRegistry(
            new INodeType[]
            {
                new PassThroughNode(),
                new IncrementNode(),
                new BranchNode(),
                new MergeNode(),
                new FailingNode(),
                new RetryableNode(),
                new SlowNode(),
                new OncePerItemNode(),
                new DelayedNode()
            },
            NullLogger<NodeRegistry>.Instance);

        var resolver = new ParameterResolver(
            NullLogger<ParameterResolver>.Instance,
            Options.Create(new JsEngineOptions()),
            new ScriptCache(Options.Create(new JsEngineOptions())));
        var contextFactory = new NodeExecutionContextFactory(
            _nodeRegistry,
            new ScriptCache(Options.Create(new JsEngineOptions())),
            resolver,
            new TestCredentialAccessor(),
            new HashSet<string>());
        var errorHandler = new ErrorStrategyHandler();

        _executionQueue = new WorkflowExecutionQueue();

        _executor = new WorkflowExecutor(
            _dbContext,
            _nodeRegistry,
            contextFactory,
            errorHandler,
            _executionQueue,
            NullLogger<WorkflowExecutor>.Instance,
            NullLogger<WorkflowSchedulerKernel>.Instance,
            new FlowEngine.Runtime.Security.SecretMasker(),
            _eventBus);
    }

    private async Task DrainAndExecuteAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(100));
            try
            {
                var item = await _executionQueue.DequeueAsync(cts.Token).ConfigureAwait(false);
                using var execDbContext = CreateDbContext();
                var workflow = await execDbContext.Workflows
                    .FirstOrDefaultAsync(w => w.Id == item.WorkflowDefinitionId, cancellationToken)
                    .ConfigureAwait(false);
                if (workflow is null) continue;

                await _executor.ExecuteLoopAsync(
                        workflow, item.ExecutionRecordId, item.TriggerPayload, execDbContext, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    [Fact]
    public async Task Linear_Workflow_Executes_All_Nodes()
    {
        var nodeA = CreateNode("a", "passThrough", isEntry: true);
        var nodeB = CreateNode("b", "increment");
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "linear",
            CreatedBy = "test",
            Nodes = [nodeA, nodeB],
            Connections =
            [
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = nodeA.Id,
                    SourcePortName = FlowConstants.PortNames.Output,
                    TargetNodeId = nodeB.Id,
                    TargetPortName = FlowConstants.PortNames.Input
                }
            ]
        };

        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var executionId = await _executor.StartAsync(workflow.Id, 5, TestContext.Current.CancellationToken);
        var record = await WaitForExecutionAsync(executionId.Value, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.Equal(2, record.NodeRecords.Count);
    }

    [Fact]
    public async Task Branch_Workflow_Routes_To_Selected_Branch()
    {
        var nodeA = CreateNode("a", "passThrough", isEntry: true);
        var nodeB = CreateNode("b", "branch", parameters: new Dictionary<string, object> { ["threshold"] = 3 });
        var nodeC = CreateNode("c", "increment");
        var nodeD = CreateNode("d", "increment");

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "branch",
            CreatedBy = "test",
            Nodes = [nodeA, nodeB, nodeC, nodeD],
            Connections =
            [
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = nodeA.Id,
                    SourcePortName = FlowConstants.PortNames.Output,
                    TargetNodeId = nodeB.Id,
                    TargetPortName = FlowConstants.PortNames.Input
                },
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = nodeB.Id,
                    SourcePortName = FlowConstants.PortNames.True,
                    TargetNodeId = nodeC.Id,
                    TargetPortName = FlowConstants.PortNames.Input
                },
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = nodeB.Id,
                    SourcePortName = FlowConstants.PortNames.False,
                    TargetNodeId = nodeD.Id,
                    TargetPortName = FlowConstants.PortNames.Input
                }
            ]
        };

        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var executionId = await _executor.StartAsync(workflow.Id, 5, TestContext.Current.CancellationToken);
        var record = await WaitForExecutionAsync(executionId.Value, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.Contains(record.NodeRecords, r => r.NodeDefinitionId == nodeC.Id);
        Assert.DoesNotContain(record.NodeRecords, r => r.NodeDefinitionId == nodeD.Id);
    }

    [Fact]
    public async Task Merge_Workflow_Waits_For_All_Inputs()
    {
        var nodeA = CreateNode("a", "passThrough", isEntry: true);
        var nodeB = CreateNode("b", "passThrough", isEntry: true);
        var nodeC = CreateNode("c", "merge");

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "merge",
            CreatedBy = "test",
            Nodes = [nodeA, nodeB, nodeC],
            Connections =
            [
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = nodeA.Id,
                    SourcePortName = FlowConstants.PortNames.Output,
                    TargetNodeId = nodeC.Id,
                    TargetPortName = "a"
                },
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = nodeB.Id,
                    SourcePortName = FlowConstants.PortNames.Output,
                    TargetNodeId = nodeC.Id,
                    TargetPortName = "b"
                }
            ]
        };

        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var executionId = await _executor.StartAsync(workflow.Id, 1, TestContext.Current.CancellationToken);
        var record = await WaitForExecutionAsync(executionId.Value, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.Contains(record.NodeRecords, r => r.NodeDefinitionId == nodeC.Id);
        var mergeRecord = record.NodeRecords.First(r => r.NodeDefinitionId == nodeC.Id);
        Assert.Equal(2, mergeRecord.Inputs.Count);
    }

    [Fact]
    public async Task Retry_Workflow_Completes_After_Retries()
    {
        var nodeA = CreateNode(
            "a",
            "retryable",
            isEntry: true,
            parameters: new Dictionary<string, object> { ["failCount"] = 2 },
            errorStrategy: ErrorStrategy.Retry,
            retryPolicy: new RetryPolicy { MaxRetries = 3, BaseDelay = TimeSpan.FromMilliseconds(10), MaxDelay = TimeSpan.FromMilliseconds(50), UseJitter = false });

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "retry",
            CreatedBy = "test",
            Nodes = [nodeA],
            Connections = []
        };

        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var executionId = await _executor.StartAsync(workflow.Id, cancellationToken: TestContext.Current.CancellationToken);
        var record = await WaitForExecutionAsync(executionId.Value, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.Single(record.NodeRecords);
        Assert.True(record.NodeRecords[0].Output.Success);
    }

    [Fact]
    public async Task Failing_Workflow_With_Terminate_Fails()
    {
        var nodeA = CreateNode("a", "failing", isEntry: true);
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "failing",
            CreatedBy = "test",
            Nodes = [nodeA],
            Connections = []
        };

        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var executionId = await _executor.StartAsync(workflow.Id, cancellationToken: TestContext.Current.CancellationToken);
        var record = await WaitForExecutionAsync(executionId.Value, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionStatus.Failed, record.Status);
    }

    [Fact]
    public async Task Cancellation_Stops_Execution_Direct()
    {
        var nodeA = CreateNode("a", "slow", isEntry: true);
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "cancel_direct",
            CreatedBy = "test",
            Nodes = [nodeA],
            Connections = []
        };

        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var executionRecord = new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Pending,
            NodeRecords = []
        };
        _dbContext.ExecutionRecords.Add(executionRecord);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _executor.ExecuteLoopAsync(
            workflow, executionRecord.Id, null,
            _dbContext, cts.Token);
        sw.Stop();

        var reloaded = await _dbContext.ExecutionRecords
            .FirstOrDefaultAsync(e => e.Id == executionRecord.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(reloaded);
        // 取消应经状态机转为 Cancelled 终态（而非被误判为 Failed）。
        Assert.Equal(ExecutionStatus.Cancelled, reloaded.Status);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"Execution took {sw.Elapsed.TotalSeconds:F1}s, expected cancellation within 5s.");
    }

    [Fact]
    public async Task CancelAsync_ViaRegistry_TransitionsRunningExecutionToCancelled()
    {
        var nodeA = CreateNode("a", "slow", isEntry: true);
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "cancel_registry",
            CreatedBy = "test",
            Nodes = [nodeA],
            Connections = []
        };

        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var executionRecord = new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Pending,
            NodeRecords = []
        };
        _dbContext.ExecutionRecords.Add(executionRecord);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // 模拟后台 worker：登记每执行取消令牌源，并将其令牌传入执行循环。
        var registry = new ExecutionCancellationRegistry();
        using var cts = new CancellationTokenSource();
        registry.Register(executionRecord.Id, cts);

        var execTask = _executor.ExecuteLoopAsync(
            workflow, executionRecord.Id, null, _dbContext, cts.Token);

        // 执行进行中经注册表触发取消（对应 ExecutionService.CancelAsync 的行为）。
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        registry.TryCancel(executionRecord.Id);

        await execTask;

        var reloaded = await _dbContext.ExecutionRecords
            .FirstOrDefaultAsync(e => e.Id == executionRecord.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(reloaded);
        Assert.Equal(ExecutionStatus.Cancelled, reloaded!.Status);
        Assert.NotNull(reloaded.CompletedAt);
    }

    [Fact]
    public async Task OncePerItem_Node_Executes_For_Each_Item()
    {
        var nodeA = CreateNode("a", "oncePerItem", isEntry: true);
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "oncePerItem",
            CreatedBy = "test",
            Nodes = [nodeA],
            Connections = []
        };

        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var executionId = await _executor.StartAsync(workflow.Id, new[] { 10, 20, 30 }, TestContext.Current.CancellationToken);
        var record = await WaitForExecutionAsync(executionId.Value, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.Equal(3, record.NodeRecords.Count);
        Assert.Equal(0, record.NodeRecords[0].RunIndex);
        Assert.Equal(1, record.NodeRecords[1].RunIndex);
        Assert.Equal(2, record.NodeRecords[2].RunIndex);
    }

    [Fact]
    public async Task Continue_Error_Strategy_Executes_Downstream_Node()
    {
        var nodeA = CreateNode("a", "failing", isEntry: true, errorStrategy: ErrorStrategy.Continue);
        var nodeB = CreateNode("b", "passThrough");

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "continue",
            CreatedBy = "test",
            Nodes = [nodeA, nodeB],
            Connections =
            [
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = nodeA.Id,
                    SourcePortName = FlowConstants.PortNames.Output,
                    TargetNodeId = nodeB.Id,
                    TargetPortName = FlowConstants.PortNames.Input
                }
            ]
        };

        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var executionId = await _executor.StartAsync(workflow.Id, cancellationToken: TestContext.Current.CancellationToken);
        var record = await WaitForExecutionAsync(executionId.Value, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.Equal(2, record.NodeRecords.Count);
        Assert.Contains(record.NodeRecords, r => r.NodeDefinitionId == nodeB.Id);
    }

    [Fact]
    public async Task Timeout_Returns_Timeout_Error_When_Node_Exceeds_Timeout()
    {
        var nodeA = CreateNode(
            "a",
            "delayed",
            isEntry: true,
            parameters: new Dictionary<string, object> { ["delayMs"] = 500 },
            timeout: TimeSpan.FromMilliseconds(100));

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "timeout",
            CreatedBy = "test",
            Nodes = [nodeA],
            Connections = []
        };

        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var executionId = await _executor.StartAsync(workflow.Id, cancellationToken: TestContext.Current.CancellationToken);
        var record = await WaitForExecutionAsync(executionId.Value, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionStatus.Failed, record.Status);
        Assert.Single(record.NodeRecords);
        Assert.Equal("Timeout", record.NodeRecords[0].Output.Error?.Code);
    }

    [Fact]
    public async Task StartAsync_EnqueuesWorkItemCarryingOnlyWorkflowDefinitionId()
    {
        var nodeA = CreateNode("a", "passThrough", isEntry: true);
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "preloaded",
            CreatedBy = "test",
            Nodes = [nodeA],
            Connections = []
        };

        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // 即使调用方传入已加载的工作流，工作项也只携带 Id，不跨作用域携带实体。
        var executionId = await _executor.StartAsync(workflow.Id, workflow, 5, TestContext.Current.CancellationToken);

        // 出队工作项，断言仅携带工作流定义 ID 与触发负载，未携带任何工作流实体。
        var item = await _executionQueue.DequeueAsync(TestContext.Current.CancellationToken);
        Assert.Equal(workflow.Id, item.WorkflowDefinitionId);
        Assert.Equal(executionId.Value, item.ExecutionRecordId);
        Assert.Equal(5, item.TriggerPayload);

        // 放回队列，交由 DrainAndExecuteAsync 实际执行，验证执行作用域重新加载路径仍正常落库。
        await _executionQueue.EnqueueAsync(item, TestContext.Current.CancellationToken);
        var record = await WaitForExecutionAsync(executionId.Value, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.Single(record.NodeRecords);
    }

    [Fact]
    public async Task StartAsync_PreloadedWorkflowIdMismatch_FallsBackToInternalLoad()
    {
        var nodeA = CreateNode("a", "passThrough", isEntry: true);
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "preloaded-mismatch",
            CreatedBy = "test",
            Nodes = [nodeA],
            Connections = []
        };

        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // 传入 Id 不匹配的预加载工作流：应回退内部加载，不抛异常且正确启动。
        // 无论哪条路径，工作项均只携带 Id（不携带实体）。
        var other = new Workflow { Id = Guid.NewGuid(), Name = "other", CreatedBy = "test", Nodes = [], Connections = [] };
        var executionId = await _executor.StartAsync(workflow.Id, other, null, TestContext.Current.CancellationToken);

        var item = await _executionQueue.DequeueAsync(TestContext.Current.CancellationToken);
        Assert.Equal(workflow.Id, item.WorkflowDefinitionId);

        await _executionQueue.EnqueueAsync(item, TestContext.Current.CancellationToken);
        var record = await WaitForExecutionAsync(executionId.Value, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ExecutionStatus.Completed, record.Status);
    }

    private static NodeDefinition CreateNode(
        string name,
        string typeName,
        bool isEntry = false,
        Dictionary<string, object>? parameters = null,
        ErrorStrategy errorStrategy = ErrorStrategy.Terminate,
        RetryPolicy? retryPolicy = null,
        TimeSpan? timeout = null)
    {
        return new NodeDefinition
        {
            Id = name,
            Name = name,
            TypeName = typeName,
            IsEntry = isEntry,
            Parameters = parameters ?? [],
            ErrorStrategy = errorStrategy,
            RetryPolicy = retryPolicy,
            Timeout = timeout
        };
    }

    private async Task<ExecutionRecord> WaitForExecutionAsync(
        Guid executionId,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        await DrainAndExecuteAsync(cancellationToken).ConfigureAwait(false);

        var maxWait = timeout ?? TimeSpan.FromSeconds(15);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        while (sw.Elapsed < maxWait)
        {
            using var readCtx = CreateDbContext();
            var record = await readCtx.ExecutionRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == executionId, cancellationToken)
                .ConfigureAwait(false);
            if (record is not null && record.Status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
            {
                return record;
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("等待执行完成超时。");
    }

    private sealed class TestCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult(new CredentialValue());
    }

    /// <summary>
    /// 记录所有已发布领域事件的虚假 <see cref="IEventBus"/>，用于断言执行器确实发布了事件（OBS-2）。
    /// </summary>
    private sealed class RecordingEventBus : IEventBus
    {
        public List<IDomainEvent> Published { get; } = new();

        public Task PublishAsync<TEvent>(TEvent eventInstance, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
        {
            Published.Add(eventInstance);
            return Task.CompletedTask;
        }

        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
            where TEvent : IDomainEvent
            => throw new NotImplementedException();
    }

    private SpyDbContext CreateSpyDbContext()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: _dbName)
            .Options;
        return new SpyDbContext(options);
    }

    private sealed class SpyDbContext : FlowEngineDbContext
    {
        public int SaveChangesAsyncCount { get; private set; }

        public SpyDbContext(DbContextOptions<FlowEngineDbContext> options) : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesAsyncCount++;
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private FlowEngineDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: _dbName)
            .Options;
        return new FlowEngineDbContext(options);
    }

    /// <summary>
    /// 验证节点记录持久化的写放大已修复：随着节点数 N 增长，SaveChangesAsync 调用次数应保持有界
    /// （约 ceil(N/25)+1），而非常规的 O(N)。同时验证终态刷新保证了全部节点记录不丢。
    /// </summary>
    [Fact]
    public async Task Linear_Chain_Of_ManyNodes_BatchesSaves_BoundedCount()
    {
        const int nodeCount = 100;
        var nodes = new List<NodeDefinition>();
        for (var i = 0; i < nodeCount; i++)
        {
            nodes.Add(CreateNode($"n{i}", "passThrough", isEntry: i == 0));
        }

        var connections = new List<Connection>();
        for (var i = 1; i < nodeCount; i++)
        {
            connections.Add(new Connection
            {
                Id = Guid.NewGuid(),
                SourceNodeId = $"n{i - 1}",
                SourcePortName = FlowConstants.PortNames.Output,
                TargetNodeId = $"n{i}",
                TargetPortName = FlowConstants.PortNames.Input
            });
        }

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "longChain",
            CreatedBy = "test",
            Nodes = nodes,
            Connections = connections
        };

        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var executionRecord = new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Pending,
            NodeRecords = []
        };
        _dbContext.ExecutionRecords.Add(executionRecord);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        using var spy = CreateSpyDbContext();
        await _executor.ExecuteLoopAsync(workflow, executionRecord.Id, null, spy, CancellationToken.None);

        using var readCtx = CreateDbContext();
        var reloaded = await readCtx.ExecutionRecords
            .AsNoTracking()
            .FirstAsync(e => e.Id == executionRecord.Id, TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionStatus.Completed, reloaded.Status);
        Assert.Equal(nodeCount, reloaded.NodeRecords.Count);

        // Pending→Running(1) + 周期性刷新(floor(N/25)) + 终态(1) ≈ N/25 + 2，留足余量断言有界且不随 N 线性增长。
        Assert.True(
            spy.SaveChangesAsyncCount <= nodeCount / 25 + 4,
            $"SaveChangesAsync 次数 {spy.SaveChangesAsyncCount} 应远小于节点数 {nodeCount}（写放大已修复）。");
        Assert.True(
            spy.SaveChangesAsyncCount < nodeCount,
            $"SaveChangesAsync 次数 {spy.SaveChangesAsyncCount} 不应随节点数线性增长。");
    }

    /// <summary>
    /// 验证失败态经 PersistFailedStateAsync 立即落库（不再使用 CancellationToken.None），
    /// 确保失败节点自身的执行记录不丢失。
    /// </summary>
    [Fact]
    public async Task Failing_Workflow_PersistsNodeRecord_ViaFailedStateFlush()
    {
        var nodeA = CreateNode("a", "failing", isEntry: true);
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "failingFlush",
            CreatedBy = "test",
            Nodes = [nodeA],
            Connections = []
        };

        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var executionId = await _executor.StartAsync(workflow.Id, cancellationToken: TestContext.Current.CancellationToken);
        var record = await WaitForExecutionAsync(executionId.Value, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionStatus.Failed, record.Status);
        // 批量刷新后，失败节点记录必须仍由失败态刷新落库。
        Assert.Single(record.NodeRecords);
        Assert.Equal("a", record.NodeRecords[0].NodeDefinitionId);
    }

    [Fact]
    public void BuildNodeExecutionRecord_MasksCredentialValueInResolvedParameters()
    {
        var credential = new CredentialValue
        {
            Name = "my-api-key",
            Type = "apiKey",
            Fields = new Dictionary<string, string> { ["token"] = "secret-token" }
        };

        var context = new NodeExecutionContext
        {
            NodeExecutionRecordId = Guid.NewGuid(),
            RawParameters = new Dictionary<string, object>(),
            ResolvedParameters = new Dictionary<string, object> { ["cred"] = credential },
            Inputs = new Dictionary<string, DataBatch>()
        };

        var resolver = new ParameterResolver(
            NullLogger<ParameterResolver>.Instance,
            Options.Create(new JsEngineOptions()),
            new ScriptCache(Options.Create(new JsEngineOptions())));
        var contextFactory = new NodeExecutionContextFactory(
            _nodeRegistry,
            new ScriptCache(Options.Create(new JsEngineOptions())),
            resolver,
            new TestCredentialAccessor(),
            new HashSet<string>());
        var errorHandler = new ErrorStrategyHandler();
        // BuildNodeExecutionRecord 已下沉至 NodeProcessor，构造其实例供反射调用。
        var processor = new NodeProcessor(
            _nodeRegistry,
            contextFactory,
            new FlowEngine.Runtime.Security.SecretMasker(),
            new RetryExecutor(new EngineDefaultsOptions(), errorHandler, NullLogger<RetryExecutor>.Instance),
            new OutputRouter(_nodeRegistry, NullLogger<OutputRouter>.Instance),
            new EngineDefaultsOptions());
        var method = typeof(NodeProcessor).GetMethod(
            "BuildNodeExecutionRecord",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(string), typeof(int), typeof(IReadOnlyDictionary<string, DataBatch>), typeof(NodeExecutionResult), typeof(NodeExecutionContext), typeof(IReadOnlySet<string>), typeof(DateTime) },
            null);
        Assert.NotNull(method);

        var record = (NodeExecutionRecord)method.Invoke(
            processor,
            new object?[]
            {
                "testNode",
                0,
                new Dictionary<string, DataBatch>(),
                new NodeExecutionResult(),
                context,
                ExecutionSession.EmptySensitiveValues,
                DateTime.UtcNow
            })!;

        var masked = Assert.IsType<Dictionary<string, object>>(record.ResolvedParameters["cred"]);
        Assert.Equal("my-api-key", masked["name"]);
        Assert.False(masked.ContainsKey("Fields"));
        Assert.False(masked.ContainsKey("fields"));
    }

    /// <summary>
    /// CON-5 回归：默认配置必须启用内存上限。修复前 MaxRetainedOutputItems 默认 0（与注释"0=不限制"一致，
    /// 但导致计划验收"大批次输出内存有上限"在默认环境下根本不生效）。
    /// </summary>
    [Fact]
    public void EngineDefaultsOptions_MaxRetainedOutputItems_HasPositiveDefault()
    {
        Assert.True(
            new EngineDefaultsOptions().MaxRetainedOutputItems > 0,
            "MaxRetainedOutputItems 默认应为正数，确保默认环境下大批次输出内存上限生效。");
    }

    /// <summary>
    /// CON-5 回归：超过上限时仅保留最新 N 项输出（旧项被丢弃），以限制常驻内存。
    /// 直接验证内核私有 CapRetainedOutput（与 BuildNodeExecutionRecord 测试一致，经反射调用）。
    /// </summary>
    [Fact]
    public void CapRetainedOutput_TruncatesToLatestMaxItems()
    {
        var resolver = new ParameterResolver(
            NullLogger<ParameterResolver>.Instance,
            Options.Create(new JsEngineOptions()),
            new ScriptCache(Options.Create(new JsEngineOptions())));
        var contextFactory = new NodeExecutionContextFactory(
            _nodeRegistry,
            new ScriptCache(Options.Create(new JsEngineOptions())),
            resolver,
            new TestCredentialAccessor(),
            new HashSet<string>());
        // CapRetainedOutput 已下沉至 NodeProcessor，构造其实例（MaxRetainedOutputItems=5）供反射调用。
        var processor = new NodeProcessor(
            _nodeRegistry,
            contextFactory,
            new FlowEngine.Runtime.Security.SecretMasker(),
            new RetryExecutor(new EngineDefaultsOptions { MaxRetainedOutputItems = 5 }, new ErrorStrategyHandler(), NullLogger<RetryExecutor>.Instance),
            new OutputRouter(_nodeRegistry, NullLogger<OutputRouter>.Instance),
            new EngineDefaultsOptions { MaxRetainedOutputItems = 5 });

        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "cap",
            CreatedBy = "test",
            Nodes = [],
            Connections = []
        };
        var execution = new ExecutionRecord { Id = Guid.NewGuid(), Status = ExecutionStatus.Pending };
        var session = new ExecutionSession(workflow, execution, execution.Id);

        var items = Enumerable.Range(0, 10)
            .Select(i => new DataItem
            {
                Data = new JsonObject { ["i"] = i },
                Success = true,
                SourceIndex = i
            })
            .ToList();
        session.SuccessfulOutputs["n"] = new DataBatch { Items = items };

        var method = typeof(NodeProcessor).GetMethod(
            "CapRetainedOutput",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        method.Invoke(processor, new object[] { session, "n" });

        Assert.Equal(5, session.SuccessfulOutputs["n"].Items.Count);
        // 保留最新 5 项（SourceIndex 5..9），最旧项被丢弃。
        Assert.Equal(5, session.SuccessfulOutputs["n"].Items[0].Data?["i"]?.GetValue<int>());
        Assert.Equal(9, session.SuccessfulOutputs["n"].Items[^1].Data?["i"]?.GetValue<int>());
    }

    /// <summary>
    /// OBS-2 回归：执行真实工作流时，执行器应经事件总线发布
    /// <see cref="WorkflowStartedEvent"/>、<see cref="NodeExecutedEvent"/>（每个节点一次）与
    /// <see cref="WorkflowCompletedEvent"/>，使审计链（执行开始/节点完成/执行结束）可被订阅。
    /// </summary>
    [Fact]
    public async Task Executor_Publishes_WorkflowStarted_NodeExecuted_And_Completed_Events()
    {
        var nodeA = CreateNode("a", "passThrough", isEntry: true);
        var nodeB = CreateNode("b", "increment");
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "events",
            CreatedBy = "test",
            Nodes = [nodeA, nodeB],
            Connections =
            [
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = nodeA.Id,
                    SourcePortName = FlowConstants.PortNames.Output,
                    TargetNodeId = nodeB.Id,
                    TargetPortName = FlowConstants.PortNames.Input
                }
            ]
        };

        _eventBus.Published.Clear();
        _dbContext.Workflows.Add(workflow);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var executionId = await _executor.StartAsync(workflow.Id, 5, TestContext.Current.CancellationToken);
        var record = await WaitForExecutionAsync(executionId.Value, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.Contains(_eventBus.Published, e => e is WorkflowStartedEvent);
        Assert.Contains(_eventBus.Published, e => e is NodeExecutedEvent);
        Assert.Contains(_eventBus.Published, e => e is WorkflowCompletedEvent);
        // 每个节点应各发布一次 NodeExecutedEvent（共 2 个节点）。
        Assert.Equal(2, _eventBus.Published.Count(e => e is NodeExecutedEvent));
    }
}
