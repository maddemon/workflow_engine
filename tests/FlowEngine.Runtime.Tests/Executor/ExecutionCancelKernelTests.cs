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
/// 内核级取消验证（修复 #1：运行中执行可被真正取消并进入 Cancelled 终态）。
/// 模拟 worker：为执行登记与关闭令牌联动的 <see cref="CancellationTokenSource"/>，
/// 将令牌传入 <see cref="WorkflowSchedulerKernel.RunAsync"/>；经注册表触发取消后，
/// 内核应捕获取消并走 <see cref="ExecutionStateMachine.Cancel()"/> 落库 Cancelled。
/// </summary>
public sealed class ExecutionCancelKernelTests
{
    private readonly INodeRegistry _nodeRegistry;
    private readonly NodeExecutionContextFactory _contextFactory;
    private readonly WorkflowSchedulerKernel _kernel;

    public ExecutionCancelKernelTests()
    {
        _nodeRegistry = new NodeRegistry([new SlowNode()], NullLogger<NodeRegistry>.Instance);

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

    // 正常路径：运行中执行被取消后，内核应将状态机推进至 Cancelled 终态。
    [Fact]
    public async Task RunAsync_RunningExecution_WhenCancellationRequested_TransitionsToCancelled()
    {
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "slow",
            CreatedBy = "test",
            Nodes = [CreateNode("slow", "slow", isEntry: true)],
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

        var registry = new ExecutionCancellationRegistry();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        registry.Register(executionRecord.Id, cts);

        var session = new ExecutionSession(workflow, executionRecord, executionRecord.Id, _nodeRegistry)
        {
            SensitiveValues = ExecutionSession.EmptySensitiveValues
        };
        var sideEffects = new NoOpSideEffects();

        // worker 驱动内核，使用登记令牌（含关闭令牌联动）。
        var kernelTask = Task.Run(() => _kernel.RunAsync(session, sideEffects, null, cts.Token));

        // 给内核一点时间进入 SlowNode 的取消可感知等待。
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // 模拟 ExecutionService.CancelAsync 经注册表取消运行中执行。
        registry.TryCancel(executionRecord.Id);

        await kernelTask;

        Assert.Equal(ExecutionStatus.Cancelled, session.Execution.Status);
        Assert.Equal(ExecutionStatus.Cancelled, session.StateMachine.Status);
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

    private sealed class NoOpSideEffects : IExecutionSideEffects
    {
        public Task PersistNodeRecordAsync(NodeExecutionRecord record, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PersistFailedStateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PersistExecutionAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishNodeStartedAsync(Guid executionId, string nodeId, int runIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishCompletedAsync(ExecutionStatus status, CancellationToken cancellationToken, NodeError? error = null) => Task.CompletedTask;
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
