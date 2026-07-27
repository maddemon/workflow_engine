using System.Linq;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Configuration;
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
/// Task 6 回归测试：<see cref="ExecutionSession.SuccessfulOutputs"/> / <see cref="ExecutionSession.LatestBatches"/>
/// 改为按 <see cref="NodeDefinition.Id"/>（稳定唯一标识）累积，且保留输出数量受
/// <see cref="EngineDefaultsOptions.MaxRetainedOutputItems"/> 上限约束（始终生效，内存有界）。
/// 修复两类问题：(1) 同名（<see cref="NodeDefinition.Name"/>）不同 Id 的节点互相覆盖/串数据；
/// (2) 未配置上限时内存无界增长。
/// </summary>
public sealed class OutputAccumulationByIdTests
{
    private readonly INodeRegistry _nodeRegistry;
    private readonly NodeExecutionContextFactory _contextFactory;
    private readonly WorkflowSchedulerKernel _kernel;

    public OutputAccumulationByIdTests()
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

    // 正确性：两个节点 Name 相同但 Id 不同，各自输出应累积在各自 Id 键下，互不被覆盖/合并。
    [Fact]
    public async Task RunAsync_SameNameDifferentId_NodesAccumulateSeparately()
    {
        // 两个入口节点共享 Name="dup"，但 Id 分别为 "dup1" 与 "dup2"。
        var nodeA = CreateNode("dup1", "dup", "passThrough", isEntry: true);
        var nodeB = CreateNode("dup2", "dup", "passThrough", isEntry: true);
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "same-name-different-id",
            CreatedBy = "test",
            Nodes = [nodeA, nodeB],
            Connections = [],
        };

        var (record, session, _) = await RunAsync(workflow, [1, 2, 3]);

        Assert.Equal(ExecutionStatus.Completed, record.Status);

        // 两个节点各自按 Id 累积，键不为 Name（"dup"）。
        Assert.False(session.SuccessfulOutputs.ContainsKey("dup"));
        Assert.True(session.SuccessfulOutputs.TryGetValue("dup1", out var outA));
        Assert.True(session.SuccessfulOutputs.TryGetValue("dup2", out var outB));

        // 二者输出互不合并：各含本节点被触发的 3 个输入项。
        Assert.Equal(3, outA.Items.Count);
        Assert.Equal(3, outB.Items.Count);

        // LatestBatches 同样按 Id 分开。
        Assert.True(session.LatestBatches.TryGetValue("dup1", out var latestA));
        Assert.True(session.LatestBatches.TryGetValue("dup2", out var latestB));
        Assert.Equal(3, latestA.Items.Count);
        Assert.Equal(3, latestB.Items.Count);
    }

    // 内存有界：节点成功输出超过 MaxRetainedOutputItems 时仅保留上限数量的项（最新 N 项）。
    [Fact]
    public async Task RunAsync_ExceedingMaxRetainedOutputItems_RetainsOnlyCap()
    {
        const int cap = 50;
        var cappedKernel = new WorkflowSchedulerKernel(
            _nodeRegistry,
            _contextFactory,
            new ErrorStrategyHandler(),
            new SecretMasker(),
            NullLogger<WorkflowSchedulerKernel>.Instance,
            Options.Create(new EngineDefaultsOptions { MaxRetainedOutputItems = cap }));

        // Id 与 Name 不同，验证限流键为 Id 而非 Name。
        var node = CreateNode("src-1", "src", "oncePerItem", isEntry: true);
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "cap-by-id",
            CreatedBy = "test",
            Nodes = [node],
            Connections = [],
        };

        var (record, session, _) = await RunAsync(workflow, Enumerable.Range(0, 500).ToArray(), cappedKernel);

        Assert.Equal(ExecutionStatus.Completed, record.Status);

        // SuccessfulOutputs 按 Id="src-1" 累积且被限流为 cap 项。
        Assert.True(session.SuccessfulOutputs.TryGetValue("src-1", out var retained));
        Assert.Equal(cap, retained.Items.Count);

        // LatestBatches 同样受 cap 约束。
        Assert.True(session.LatestBatches.TryGetValue("src-1", out var latest));
        Assert.Equal(cap, latest.Items.Count);

        // 保留的是最新 N 项（SourceIndex 451..500），最旧项被丢弃。
        Assert.Equal(500 - cap, retained.Items[0].SourceIndex);
        Assert.Equal(499, retained.Items[^1].SourceIndex);
    }

    // 默认即生效：未显式配置（取默认 1000）时，超量输出仍被截断，而非无界增长。
    [Fact]
    public async Task RunAsync_DefaultCapAppliedWhenNotConfigured()
    {
        var node = CreateNode("dft-1", "dft", "oncePerItem", isEntry: true);
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "default-cap",
            CreatedBy = "test",
            Nodes = [node],
            Connections = [],
        };

        var (record, session, _) = await RunAsync(workflow, Enumerable.Range(0, 1500).ToArray());

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.True(session.SuccessfulOutputs.TryGetValue("dft-1", out var retained));
        Assert.Equal(EngineDefaultsOptions.DefaultMaxRetainedOutputItems, retained.Items.Count);
    }

    private async Task<(ExecutionRecord Record, ExecutionSession Session, CollectingSideEffects SideEffects)> RunAsync(
        Workflow workflow, int[] items, WorkflowSchedulerKernel? kernel = null)
    {
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
        await (kernel ?? _kernel).RunAsync(session, sideEffects, items, TestContext.Current.CancellationToken);

        return (executionRecord, session, sideEffects);
    }

    private static NodeDefinition CreateNode(
        string id,
        string name,
        string typeName,
        bool isEntry = false,
        ErrorStrategy errorStrategy = ErrorStrategy.Terminate)
    {
        return new NodeDefinition
        {
            Id = id,
            Name = name,
            TypeName = typeName,
            IsEntry = isEntry,
            Parameters = [],
            ErrorStrategy = errorStrategy,
        };
    }

    private sealed class CollectingSideEffects : IExecutionSideEffects
    {
        public Task PersistNodeRecordAsync(NodeExecutionRecord record, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PersistFailedStateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PersistExecutionAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishNodeStartedAsync(Guid executionId, string nodeId, int runIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishCompletedAsync(ExecutionStatus status, CancellationToken cancellationToken, NodeError? error = null) => Task.CompletedTask;
        public Task PublishWorkflowStartedAsync(Guid executionId, Guid workflowDefinitionId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishNodeExecutedAsync(Guid executionId, string nodeDefinitionId, int runIndex, NodeExecutionResult result, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishNodeErrorAsync(Guid executionId, string nodeDefinitionId, int runIndex, NodeError error, CancellationToken cancellationToken) => Task.CompletedTask;
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
