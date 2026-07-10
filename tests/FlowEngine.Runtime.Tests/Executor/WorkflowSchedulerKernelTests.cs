using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Registry;
using FlowEngine.Runtime.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlowEngine.Runtime.Tests.Executor;

/// <summary>
/// <see cref="WorkflowSchedulerKernel"/> 独立单测：在纯内存（无 DbContext / 事件总线）下验证调度逻辑，
/// 并验证其可在普通执行与 Dry-Run 两外壳间共享。
/// </summary>
public sealed class WorkflowSchedulerKernelTests
{
    private readonly INodeRegistry _nodeRegistry;
    private readonly NodeExecutionContextFactory _contextFactory;
    private readonly WorkflowSchedulerKernel _kernel;

    public WorkflowSchedulerKernelTests()
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
                new OncePerItemNode(),
                new DelayedNode(),
                new SlowNode()
            },
            NullLogger<NodeRegistry>.Instance);

        var resolver = new ParameterResolver(NullLogger<ParameterResolver>.Instance);
        _contextFactory = new NodeExecutionContextFactory(
            _nodeRegistry,
            new ScriptCache(Options.Create(new JsEngineOptions())),
            resolver,
            new StubCredentialAccessor(),
            new HashSet<string>());
        _kernel = new WorkflowSchedulerKernel(
            _nodeRegistry, _contextFactory, new ErrorStrategyHandler(), new SecretMasker(), NullLogger<WorkflowSchedulerKernel>.Instance);
    }

    [Fact]
    public async Task RunAsync_LinearWorkflow_ProducesRecords_AndCompletes()
    {
        var nodeA = CreateNode("a", "passThrough", isEntry: true);
        var nodeB = CreateNode("b", "passThrough");
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

        var (record, sideEffects) = await RunAsync(workflow, 5);

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.Equal(2, record.NodeRecords.Count);
        Assert.Contains(record.NodeRecords, r => r.NodeDefinitionId == nodeA.Id);
        Assert.Contains(record.NodeRecords, r => r.NodeDefinitionId == nodeB.Id);
        Assert.True(sideEffects.PersistCalls > 0, "内核应触发持久化副作用回调。");
    }

    [Fact]
    public async Task RunAsync_BranchWorkflow_RoutesToSelectedBranch()
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
                new Connection { Id = Guid.NewGuid(), SourceNodeId = nodeA.Id, SourcePortName = FlowConstants.PortNames.Output, TargetNodeId = nodeB.Id, TargetPortName = FlowConstants.PortNames.Input },
                new Connection { Id = Guid.NewGuid(), SourceNodeId = nodeB.Id, SourcePortName = FlowConstants.PortNames.True, TargetNodeId = nodeC.Id, TargetPortName = FlowConstants.PortNames.Input },
                new Connection { Id = Guid.NewGuid(), SourceNodeId = nodeB.Id, SourcePortName = FlowConstants.PortNames.False, TargetNodeId = nodeD.Id, TargetPortName = FlowConstants.PortNames.Input }
            ]
        };

        var (record, _) = await RunAsync(workflow, 5);

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.Contains(record.NodeRecords, r => r.NodeDefinitionId == nodeC.Id);
        Assert.DoesNotContain(record.NodeRecords, r => r.NodeDefinitionId == nodeD.Id);
    }

    [Fact]
    public async Task RunAsync_FailingNode_TerminatesWithFailed()
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

        var (record, _) = await RunAsync(workflow);

        Assert.Equal(ExecutionStatus.Failed, record.Status);
        Assert.Single(record.NodeRecords);
    }

    [Fact]
    public async Task RunAsync_OncePerItem_ExecutesForEachItem()
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

        var (record, _) = await RunAsync(workflow, new[] { 10, 20, 30 });

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.Equal(3, record.NodeRecords.Count);
        Assert.Equal(0, record.NodeRecords[0].RunIndex);
        Assert.Equal(1, record.NodeRecords[1].RunIndex);
        Assert.Equal(2, record.NodeRecords[2].RunIndex);
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

        var method = typeof(WorkflowSchedulerKernel).GetMethod(
            "BuildNodeExecutionRecord",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null,
            new[] { typeof(Guid), typeof(int), typeof(IReadOnlyDictionary<string, DataBatch>), typeof(NodeExecutionResult), typeof(NodeExecutionContext), typeof(IReadOnlySet<string>) },
            null);
        Assert.NotNull(method);

        var record = (NodeExecutionRecord)method.Invoke(
            _kernel,
            new object?[]
            {
                Guid.NewGuid(),
                0,
                new Dictionary<string, DataBatch>(),
                new NodeExecutionResult(),
                context,
                ExecutionSession.EmptySensitiveValues
            })!;

        var masked = Assert.IsType<Dictionary<string, object>>(record.ResolvedParameters["cred"]);
        Assert.Equal("my-api-key", masked["name"]);
        Assert.False(masked.ContainsKey("Fields"));
        Assert.False(masked.ContainsKey("fields"));
    }

    private async Task<(ExecutionRecord Record, CollectingSideEffects SideEffects)> RunAsync(
        Workflow workflow,
        object? triggerPayload = null)
    {
        var executionRecord = new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Pending,
            NodeRecords = []
        };

        var session = new ExecutionSession(workflow, executionRecord, executionRecord.Id)
        {
            SensitiveValues = ExecutionSession.EmptySensitiveValues
        };

        var sideEffects = new CollectingSideEffects();
        await _kernel.RunAsync(session, sideEffects, triggerPayload, TestContext.Current.CancellationToken)
            .ConfigureAwait(false);

        return (executionRecord, sideEffects);
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
            Id = Guid.NewGuid(),
            Name = name,
            TypeName = typeName,
            IsEntry = isEntry,
            Parameters = parameters ?? [],
            ErrorStrategy = errorStrategy,
            RetryPolicy = retryPolicy,
            Timeout = timeout
        };
    }

    private sealed class CollectingSideEffects : IExecutionSideEffects
    {
        public int PersistCalls { get; private set; }

        public Task PersistNodeRecordAsync(NodeExecutionRecord record, CancellationToken cancellationToken)
        {
            PersistCalls++;
            return Task.CompletedTask;
        }

        public Task PersistFailedStateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PersistExecutionAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishNodeStartedAsync(Guid executionId, Guid nodeId, int runIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishCompletedAsync(ExecutionStatus status, CancellationToken cancellationToken) => Task.CompletedTask;
        public Func<LlmStreamChunk, CancellationToken, Task> CreateLlmStreamCallback(Guid executionId, Guid nodeId, int runIndex)
            => (_, _) => Task.CompletedTask;
    }

    private sealed class StubCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult(new CredentialValue());

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue?>(null);
    }
}
