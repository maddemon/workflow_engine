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
/// OncePerItem 输出累积验证（修复 #5：逐项运行覆盖式赋值只保留最后一项）。
/// 修复后每次运行的输出应追加到累积批，下游节点据此拿到全部项输出，而非仅最后一项。
/// </summary>
public sealed class OncePerItemAccumulationTests
{
    private readonly INodeRegistry _nodeRegistry;
    private readonly NodeExecutionContextFactory _contextFactory;
    private readonly WorkflowSchedulerKernel _kernel;

    public OncePerItemAccumulationTests()
    {
        _nodeRegistry = new NodeRegistry(
            [new PassThroughNode(), new OncePerItemNode()],
            NullLogger<NodeRegistry>.Instance);

        var resolver = new ParameterResolver(
            NullLogger<ParameterResolver>.Instance,
            Options.Create(new JsEngineOptions()),
            new ScriptCache(Options.Create(new JsEngineOptions())));
        _contextFactory = new NodeExecutionContextFactory(
            _nodeRegistry,
            new ScriptCache(Options.Create(new JsEngineOptions())),
            resolver,
            new StubCredentialAccessor(),
            new HashSet<string>());
        _kernel = new WorkflowSchedulerKernel(
            _nodeRegistry, _contextFactory, new ErrorStrategyHandler(), new SecretMasker(), NullLogger<WorkflowSchedulerKernel>.Instance);
    }

    // 边界：单输入项时 OncePerItem 节点输出累积为 1 项（不丢项、不重复）。
    [Fact]
    public async Task RunAsync_OncePerItem_SingleItem_ProducesOneOutput()
    {
        var (record, session, _) = await RunOncePerItemAsync([42]);

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.True(session.SuccessfulOutputs.TryGetValue("a", out var outputs));
        Assert.Single(outputs.Items);
        Assert.Equal(0, outputs.Items[0].SourceIndex);
    }

    // 正常路径：多输入项时 OncePerItem 节点所有运行输出被累积到 session.SuccessfulOutputs，
    // 下游经 $node.<name> 读取时能拿到全部项（而非仅最后一项）。
    [Fact]
    public async Task RunAsync_OncePerItem_AccumulatesAllItemOutputsPreservingContent()
    {
        var (record, session, _) = await RunOncePerItemAsync([10, 20, 30]);

        Assert.Equal(ExecutionStatus.Completed, record.Status);

        // 源节点 a 的 3 个输入项各自运行一次，输出应被累积为 3 项（而非被覆盖为 1 项）。
        Assert.True(session.SuccessfulOutputs.TryGetValue("a", out var outputs));
        Assert.Equal(3, outputs.Items.Count);

        // 累积内容完整：每次运行的输出（Data == RunIndex）按 SourceIndex 0/1/2 保留，无覆盖丢失。
        Assert.Contains(outputs.Items, i => i.SourceIndex == 0 && i.Data?.GetValue<int>() == 0);
        Assert.Contains(outputs.Items, i => i.SourceIndex == 1 && i.Data?.GetValue<int>() == 1);
        Assert.Contains(outputs.Items, i => i.SourceIndex == 2 && i.Data?.GetValue<int>() == 2);
    }

    // FIX 1 回归：OncePerItem 源节点经 EDGE 路由到下游收集节点时，
    // 下游应收到累积批（全部项），而非仅最后一次运行的单批（静默丢数据）。
    [Fact]
    public async Task RunAsync_OncePerItem_RoutesCumulativeBatchToEdgeDownstream()
    {
        var (record, session, _) = await RunOncePerItemWithEdgeAsync([10, 20, 30]);

        Assert.Equal(ExecutionStatus.Completed, record.Status);

        // 下游节点 b 经 EDGE 收到的输入即其 PassThrough 输出：修复后应为累积批（3 项），
        // 若为 bug（仅最后一次运行的单批）则只有 1 项。
        Assert.True(session.SuccessfulOutputs.TryGetValue("b", out var downstreamOutputs));
        Assert.Equal(3, downstreamOutputs.Items.Count);
        Assert.Contains(downstreamOutputs.Items, i => i.SourceIndex == 0 && i.Data?.GetValue<int>() == 0);
        Assert.Contains(downstreamOutputs.Items, i => i.SourceIndex == 1 && i.Data?.GetValue<int>() == 1);
        Assert.Contains(downstreamOutputs.Items, i => i.SourceIndex == 2 && i.Data?.GetValue<int>() == 2);
    }

    private async Task<(ExecutionRecord Record, ExecutionSession Session, CollectingSideEffects SideEffects)> RunOncePerItemWithEdgeAsync(int[] items)
    {
        var nodeA = CreateNode("a", "oncePerItem", isEntry: true);
        var nodeB = CreateNode("b", "passThrough", isEntry: false);
        var connection = new Connection
        {
            SourceNodeId = "a",
            TargetNodeId = "b",
            SourcePortName = "output",
            TargetPortName = "input",
        };
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "oncePerItem-edge-routing",
            CreatedBy = "test",
            Nodes = [nodeA, nodeB],
            Connections = [connection],
        };

        var executionRecord = new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Pending,
            NodeRecords = [],
        };

        var session = new ExecutionSession(workflow, executionRecord, executionRecord.Id, _nodeRegistry)
        {
            SensitiveValues = ExecutionSession.EmptySensitiveValues
        };

        var sideEffects = new CollectingSideEffects();
        await _kernel.RunAsync(session, sideEffects, items, TestContext.Current.CancellationToken);

        return (executionRecord, session, sideEffects);
    }

    private async Task<(ExecutionRecord Record, ExecutionSession Session, CollectingSideEffects SideEffects)> RunOncePerItemAsync(int[] items)
    {
        var nodeA = CreateNode("a", "oncePerItem", isEntry: true);
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "oncePerItem-accumulate",
            CreatedBy = "test",
            Nodes = [nodeA],
            Connections = [],
        };

        var executionRecord = new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Pending,
            NodeRecords = [],
        };

        var session = new ExecutionSession(workflow, executionRecord, executionRecord.Id, _nodeRegistry)
        {
            SensitiveValues = ExecutionSession.EmptySensitiveValues
        };

        var sideEffects = new CollectingSideEffects();
        await _kernel.RunAsync(session, sideEffects, items, TestContext.Current.CancellationToken);

        return (executionRecord, session, sideEffects);
    }

    private static NodeDefinition CreateNode(
        string name,
        string typeName,
        bool isEntry = false,
        ErrorStrategy errorStrategy = ErrorStrategy.Terminate)
    {
        return new NodeDefinition
        {
            Id = name,
            Name = name,
            TypeName = typeName,
            IsEntry = isEntry,
            Parameters = [],
            ErrorStrategy = errorStrategy,
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
        public Task PublishNodeStartedAsync(Guid executionId, string nodeId, int runIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishCompletedAsync(ExecutionStatus status, CancellationToken cancellationToken) => Task.CompletedTask;
        public Func<LlmStreamChunk, CancellationToken, Task> CreateLlmStreamCallback(Guid executionId, string nodeId, int runIndex)
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
