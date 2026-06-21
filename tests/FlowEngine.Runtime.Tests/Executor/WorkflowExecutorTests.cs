using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Runtime.Tests.Executor;

public class WorkflowExecutorTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly FlowEngineDbContext _dbContext;
    private readonly INodeRegistry _nodeRegistry;
    private readonly WorkflowExecutor _executor;
    private readonly WorkflowExecutionQueue _executionQueue;

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
                new OncePerItemNode()
            },
            NullLogger<NodeRegistry>.Instance);

        var resolver = new ParameterResolver(
            NullLogger<ParameterResolver>.Instance);
        var contextFactory = new NodeExecutionContextFactory(_nodeRegistry, resolver, new TestCredentialAccessor(), new HashSet<string>());
        var errorHandler = new ErrorStrategyHandler();

        _executionQueue = new WorkflowExecutionQueue();

        _executor = new WorkflowExecutor(
            _dbContext,
            _nodeRegistry,
            contextFactory,
            errorHandler,
            _executionQueue,
            NullLogger<WorkflowExecutor>.Instance);
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
                    SourcePortName = "output",
                    TargetNodeId = nodeB.Id,
                    TargetPortName = "input"
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
                    SourcePortName = "output",
                    TargetNodeId = nodeB.Id,
                    TargetPortName = "input"
                },
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = nodeB.Id,
                    SourcePortName = "true",
                    TargetNodeId = nodeC.Id,
                    TargetPortName = "input"
                },
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = nodeB.Id,
                    SourcePortName = "false",
                    TargetNodeId = nodeD.Id,
                    TargetPortName = "input"
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
                    SourcePortName = "output",
                    TargetNodeId = nodeC.Id,
                    TargetPortName = "a"
                },
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = nodeB.Id,
                    SourcePortName = "output",
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
        Assert.True(
            reloaded.Status is ExecutionStatus.Cancelled or ExecutionStatus.Failed,
            $"Expected cancelled or failed, but was {reloaded.Status}.");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"Execution took {sw.Elapsed.TotalSeconds:F1}s, expected cancellation within 5s.");
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
                    SourcePortName = "output",
                    TargetNodeId = nodeB.Id,
                    TargetPortName = "input"
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

    private static NodeDefinition CreateNode(
        string name,
        string typeName,
        bool isEntry = false,
        Dictionary<string, object>? parameters = null,
        ErrorStrategy errorStrategy = ErrorStrategy.Terminate,
        RetryPolicy? retryPolicy = null)
    {
        return new NodeDefinition
        {
            Id = Guid.NewGuid(),
            Name = name,
            TypeName = typeName,
            IsEntry = isEntry,
            Parameters = parameters ?? [],
            ErrorStrategy = errorStrategy,
            RetryPolicy = retryPolicy
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

    private FlowEngineDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseInMemoryDatabase(databaseName: _dbName)
            .Options;
        return new FlowEngineDbContext(options);
    }
}
