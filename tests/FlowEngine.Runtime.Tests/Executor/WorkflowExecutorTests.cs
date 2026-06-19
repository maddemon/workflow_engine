using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Registry;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowEngine.Runtime.Tests.Executor;

public class WorkflowExecutorTests
{
    private readonly InMemoryWorkflowRepository _workflowRepository = new();
    private readonly InMemoryExecutionStore _executionStore = new();
    private readonly INodeRegistry _nodeRegistry;
    private readonly WorkflowExecutor _executor;

    public WorkflowExecutorTests()
    {
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

        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var evaluator = new ExpressionEvaluator(memoryCache);
        var resolver = new ParameterResolver(
            evaluator,
            NullLogger<ParameterResolver>.Instance);
        var contextFactory = new NodeExecutionContextFactory(_nodeRegistry, evaluator, resolver, new TestCredentialAccessor(), new HashSet<string>());
        var errorHandler = new ErrorStrategyHandler();

        _executor = new WorkflowExecutor(
            _workflowRepository,
            _executionStore,
            _nodeRegistry,
            contextFactory,
            errorHandler,
            NullLogger<WorkflowExecutor>.Instance);
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

        await _workflowRepository.SaveAsync(workflow, TestContext.Current.CancellationToken);
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

        await _workflowRepository.SaveAsync(workflow, TestContext.Current.CancellationToken);
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

        await _workflowRepository.SaveAsync(workflow, TestContext.Current.CancellationToken);
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

        await _workflowRepository.SaveAsync(workflow, TestContext.Current.CancellationToken);
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

        await _workflowRepository.SaveAsync(workflow, TestContext.Current.CancellationToken);
        var executionId = await _executor.StartAsync(workflow.Id, cancellationToken: TestContext.Current.CancellationToken);
        var record = await WaitForExecutionAsync(executionId.Value, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionStatus.Failed, record.Status);
    }

    [Fact]
    public async Task Cancellation_Stops_Execution()
    {
        var nodeA = CreateNode("a", "slow", isEntry: true);
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "cancel",
            CreatedBy = "test",
            Nodes = [nodeA],
            Connections = []
        };

        await _workflowRepository.SaveAsync(workflow, TestContext.Current.CancellationToken);
        using var cts = new CancellationTokenSource();
        var executionId = await _executor.StartAsync(workflow.Id, cancellationToken: cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        var record = await WaitForExecutionAsync(executionId.Value, timeout: TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(
            record.Status == ExecutionStatus.Cancelled || record.Status == ExecutionStatus.Failed,
            $"Expected cancelled or failed, but was {record.Status}.");
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

        await _workflowRepository.SaveAsync(workflow, TestContext.Current.CancellationToken);
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

        await _workflowRepository.SaveAsync(workflow, TestContext.Current.CancellationToken);
        var executionId = await _executor.StartAsync(workflow.Id, cancellationToken: TestContext.Current.CancellationToken);
        var record = await WaitForExecutionAsync(executionId.Value, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.Equal(2, record.NodeRecords.Count);
        Assert.Contains(record.NodeRecords, r => r.NodeDefinitionId == nodeB.Id);
    }

    private static NodeInstance CreateNode(
        string name,
        string typeName,
        bool isEntry = false,
        Dictionary<string, object>? parameters = null,
        ErrorStrategy errorStrategy = ErrorStrategy.Terminate,
        RetryPolicy? retryPolicy = null)
    {
        return new NodeInstance
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
        var maxWait = timeout ?? TimeSpan.FromSeconds(5);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        while (sw.Elapsed < maxWait)
        {
            var record = await _executionStore.GetByIdAsync(executionId, cancellationToken).ConfigureAwait(false);
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
}
