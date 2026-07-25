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
                new SlowNode(),
                new BadScriptNode(),
                new NoteNode()
            },
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

    // CON-6：调度空闲循环由 SchedulerWake 信号事件驱动唤醒，取代固定 500ms 空轮询。
    // 验证唤醒原语：PulseScheduler 释放信号（可唤醒阻塞的 WaitAsync），且信号计数不超过 1（防护无界增长）。
    [Fact]
    public void SchedulerWake_PulseScheduler_ReleasesAndIsBounded()
    {
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "wake",
            CreatedBy = "t",
            Nodes = [],
            Connections = []
        };
        var record = new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Pending,
            NodeRecords = []
        };
        var session = new ExecutionSession(workflow, record, record.Id);

        Assert.Equal(0, session.SchedulerWake.CurrentCount);

        // 入队后脉冲：信号被释放，阻塞的 WaitAsync 可立即完成（事件驱动唤醒，无 500ms 忙等）。
        session.PulseScheduler();
        Assert.Equal(1, session.SchedulerWake.CurrentCount);
        Assert.True(session.SchedulerWake.WaitAsync(CancellationToken.None).IsCompletedSuccessfully);

        // 连续脉冲不使计数超过 1：避免空闲期间信号无界累积。
        session.PulseScheduler();
        session.PulseScheduler();
        Assert.Equal(1, session.SchedulerWake.CurrentCount);
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

        var (record, _, sideEffects) = await RunAsync(workflow, 5);

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

        var (record, _, _) = await RunAsync(workflow, 5);

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

        var (record, _, _) = await RunAsync(workflow);

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

        var (record, _, _) = await RunAsync(workflow, new[] { 10, 20, 30 });

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.Equal(3, record.NodeRecords.Count);
        Assert.Equal(0, record.NodeRecords[0].RunIndex);
        Assert.Equal(1, record.NodeRecords[1].RunIndex);
        Assert.Equal(2, record.NodeRecords[2].RunIndex);
    }

    // Task 5：OncePerItem 多次运行应累积全部项输出到 session.SuccessfulOutputs，
    // 下游 $node.<name> 才能拿到全部项，而非仅最后一项（修复覆盖式赋值）。
    [Fact]
    public async Task RunAsync_OncePerItem_AccumulatesAllItemOutputs()
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

        var (record, session, _) = await RunAsync(workflow, new[] { 10, 20, 30 });

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.True(session.SuccessfulOutputs.TryGetValue("a", out var outputs));
        // 3 个输入项各自运行一次，输出应被累积为 3 项，而非被覆盖为 1 项。
        Assert.Equal(3, outputs.Items.Count);
        Assert.Contains(outputs.Items, i => i.SourceIndex == 0);
        Assert.Contains(outputs.Items, i => i.SourceIndex == 1);
        Assert.Contains(outputs.Items, i => i.SourceIndex == 2);
    }

    // Task 5：预求值阶段 Script 编译失败应被内核捕获，记录带节点/参数信息的失败记录（而非裸奔/崩溃）。
    [Fact]
    public async Task RunAsync_BadScriptParameter_PreEvaluationFailureRecordsScriptError()
    {
        var node = CreateNode("bad", "badScript", isEntry: true,
            parameters: new Dictionary<string, object> { ["code"] = new Script { Source = "return (" } });
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "badScript",
            CreatedBy = "test",
            Nodes = [node],
            Connections = []
        };

        var (record, _, _) = await RunAsync(workflow);

        // 内核捕获 ScriptErrorException 并写入结构性失败记录（含节点 ID 与错误码），不向上抛崩溃。
        var failed = Assert.Single(record.NodeRecords);
        Assert.Equal("bad", failed.NodeDefinitionId);
        Assert.False(failed.Output.Success);
        Assert.NotNull(failed.Output.Error);
        Assert.Equal("ScriptParameterPreEvaluationError", failed.Output.Error!.Code);
        // EX-2：预求值脚本错误不得向客户端泄露原始异常文本或源码片段（如 "return ("）。
        Assert.Equal(NodeErrorFactory.SafeMessage, failed.Output.Error!.Message);
        Assert.DoesNotContain("return (", failed.Output.Error!.Message);
    }

    // Task ENG2：零端口（纯注释）节点既无输入也无输出端口，不应被入队/执行，
    // 不产出任何 NodeExecutionRecord，且执行整体仍正常完成。
    [Fact]
    public async Task RunAsync_ZeroPortNode_IsSkipped_NoRecordProduced()
    {
        var note = CreateNode("note1", "note");
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "note-only",
            CreatedBy = "test",
            Nodes = [note],
            Connections = []
        };

        var (record, session, _) = await RunAsync(workflow);

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.Empty(record.NodeRecords);
        Assert.False(session.SuccessfulOutputs.ContainsKey("note1"));
    }

    // Task ENG2：回归——无入口标记、无输入连接的普通节点仍按隐式入口入队执行。
    [Fact]
    public async Task RunAsync_NormalNodeWithoutEntryOrInput_StillEnqueuedAsImplicitEntry()
    {
        var node = CreateNode("plain", "passThrough");
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "implicit-entry",
            CreatedBy = "test",
            Nodes = [node],
            Connections = []
        };

        var (record, _, _) = await RunAsync(workflow);

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.Contains(record.NodeRecords, r => r.NodeDefinitionId == node.Id);
    }

    // Task ENG2：回归——带输出端口的触发器（DefaultIsEntry=true）仍作为入口执行。
    [Fact]
    public async Task RunAsync_TriggerWithOutputPorts_StillExecutes()
    {
        var trigger = CreateNode("trigger", "failing"); // FailingNode.DefaultIsEntry = true
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "trigger",
            CreatedBy = "test",
            Nodes = [trigger],
            Connections = []
        };

        var (record, _, _) = await RunAsync(workflow);

        // 触发器（failing）仍被入队并执行，记录存在且状态为 Failed。
        Assert.Single(record.NodeRecords);
        Assert.Equal(ExecutionStatus.Failed, record.Status);
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
            new[] { typeof(string), typeof(int), typeof(IReadOnlyDictionary<string, DataBatch>), typeof(NodeExecutionResult), typeof(NodeExecutionContext), typeof(IReadOnlySet<string>) },
            null);
        Assert.NotNull(method);

        var record = (NodeExecutionRecord)method.Invoke(
            _kernel,
            new object?[]
            {
                "test-node",
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

    private async Task<(ExecutionRecord Record, ExecutionSession Session, CollectingSideEffects SideEffects)> RunAsync(
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

        var session = new ExecutionSession(workflow, executionRecord, executionRecord.Id, _nodeRegistry)
        {
            SensitiveValues = ExecutionSession.EmptySensitiveValues
        };

        var sideEffects = new CollectingSideEffects();
        await _kernel.RunAsync(session, sideEffects, triggerPayload, TestContext.Current.CancellationToken)
            .ConfigureAwait(false);

        return (executionRecord, session, sideEffects);
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
