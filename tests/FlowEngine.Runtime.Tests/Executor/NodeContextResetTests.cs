using System.Collections.Generic;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Registry;
using FlowEngine.Runtime.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlowEngine.Runtime.Tests.Executor;

/// <summary>
/// 节点上下文生命周期（Task 9）：回边激活复用同一上下文实例；非回边（新上游输入）激活重置为新实例。
/// 通过捕获每次激活时的 <c>context.NodeContext</c> 引用并比对实例身份来验证。
/// </summary>
public sealed class NodeContextResetTests
{
    [Fact]
    public async Task FeedbackActivation_ReusesSameNodeContextInstance()
    {
        // 自环节点：每次回环激活复用同一上下文，故累计计数持续，捕获的实例唯一。
        var obs = new LoopObservingNode();
        var registry = new NodeRegistry(new INodeType[] { obs }, NullLogger<NodeRegistry>.Instance);

        var (record, _, _) = await RunAsync(BuildSelfLoopWorkflow(), registry);

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        // 3 次 Loop + 1 次 Done = 4 次激活。
        Assert.Equal(4, record.NodeRecords.Count(r => r.NodeDefinitionId == "obs"));
        // 回环激活均复用同一上下文实例。
        Assert.Single(obs.Captured.Distinct());
        // 计数累积到 MaxLoops，证明上下文状态跨调用保持。
        Assert.Equal(LoopObservingNode.MaxLoops, obs.FinalCallCount);
    }

    [Fact]
    public async Task NonFeedbackActivation_ResetsNodeContextInstance()
    {
        // 两个独立上游（A、B）各触发一次 obs，均为非回边激活 → 上下文分别重置（不同实例）。
        var obs = new ForkObservingNode();
        var registry = new NodeRegistry(new INodeType[] { new SourceNode(), obs }, NullLogger<NodeRegistry>.Instance);

        var (record, _, _) = await RunAsync(BuildForkWorkflow(), registry);

        Assert.Equal(ExecutionStatus.Completed, record.Status);
        Assert.Equal(2, record.NodeRecords.Count(r => r.NodeDefinitionId == "obs"));
        // 两次激活上下文实例不同（每次非回边均重置）。
        Assert.True(obs.Captured.Distinct().Count() == 2, "expected two distinct contexts after non-feedback resets");
    }

    private static Workflow BuildSelfLoopWorkflow()
    {
        var obs = new NodeDefinition { Id = "obs", TypeName = "obs", Name = "obs", IsEntry = true };
        return new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "selfLoop",
            CreatedBy = "test",
            Nodes = [obs],
            Connections =
            [
                new Connection { Id = Guid.NewGuid(), SourceNodeId = "obs", SourcePortName = FlowConstants.PortNames.Loop, TargetNodeId = "obs", TargetPortName = FlowConstants.PortNames.Input }
            ]
        };
    }

    private static Workflow BuildForkWorkflow()
    {
        var a = new NodeDefinition { Id = "a", TypeName = "source", Name = "a", IsEntry = true };
        var b = new NodeDefinition { Id = "b", TypeName = "source", Name = "b", IsEntry = true };
        var obs = new NodeDefinition { Id = "obs", TypeName = "obs", Name = "obs" };
        return new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "fork",
            CreatedBy = "test",
            Nodes = [a, b, obs],
            Connections =
            [
                new Connection { Id = Guid.NewGuid(), SourceNodeId = "a", SourcePortName = FlowConstants.PortNames.Output, TargetNodeId = "obs", TargetPortName = FlowConstants.PortNames.Input },
                new Connection { Id = Guid.NewGuid(), SourceNodeId = "b", SourcePortName = FlowConstants.PortNames.Output, TargetNodeId = "obs", TargetPortName = FlowConstants.PortNames.Input }
            ]
        };
    }

    private async Task<(ExecutionRecord Record, ExecutionSession Session, CollectingSideEffects SideEffects)> RunAsync(
        Workflow workflow,
        INodeRegistry registry)
    {
        var resolver = new ParameterResolver(
            NullLogger<ParameterResolver>.Instance,
            Options.Create(new JsEngineOptions()),
            new ScriptCache(Options.Create(new JsEngineOptions())));
        var factory = new NodeExecutionContextFactory(
            registry,
            new ScriptCache(Options.Create(new JsEngineOptions())),
            resolver,
            new StubCredentialAccessor(),
            new HashSet<string>());
        var kernel = new WorkflowSchedulerKernel(
            registry, factory, new ErrorStrategyHandler(), new SecretMasker(), NullLogger<WorkflowSchedulerKernel>.Instance);

        var record = new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Pending,
            NodeRecords = []
        };

        var session = new ExecutionSession(workflow, record, record.Id, registry)
        {
            SensitiveValues = ExecutionSession.EmptySensitiveValues
        };

        var sideEffects = new CollectingSideEffects();
        var trigger = new DataBatch
        {
            Items = [new DataItem { Data = 1, Success = true, SourceIndex = 0 }]
        };
        await kernel.RunAsync(session, sideEffects, trigger, TestContext.Current.CancellationToken)
            .ConfigureAwait(false);

        return (record, session, sideEffects);
    }

    /// <summary>
    /// 自环观察节点：累计激活次数到节点上下文，前 <see cref="MaxLoops"/> 次走 Loop 端口，其后走 Done。
    /// 捕获每次激活的 <c>context.NodeContext</c> 引用以验证复用。
    /// </summary>
    private sealed class LoopObservingNode : INodeType
    {
        public const int MaxLoops = 3;

        public List<IDictionary<string, object?>> Captured { get; } = [];
        public int FinalCallCount { get; private set; }

        public string TypeName => "obs";
        public string DisplayName => "Obs";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports { get; } =
        [
            new PortDefinition { Name = FlowConstants.PortNames.Input, Direction = PortDirection.Input, Type = PortType.Main },
            new PortDefinition { Name = FlowConstants.PortNames.Loop, Direction = PortDirection.Output, Type = PortType.Main },
            new PortDefinition { Name = FlowConstants.PortNames.Done, Direction = PortDirection.Output, Type = PortType.Main }
        ];
        public bool DefaultIsEntry => false;

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
        {
            Captured.Add(context.NodeContext);
            var calls = context.NodeContext.GetValue("calls") is int c ? c : 0;
            if (calls < MaxLoops)
            {
                context.NodeContext["calls"] = calls + 1;
                return Task.FromResult(new NodeExecutionResult
                {
                    Success = true,
                    Output = new DataBatch { Items = [new DataItem { Data = 1, Success = true, SourceIndex = 0 }] },
                    BranchIndex = 0
                });
            }

            FinalCallCount = calls;
            return Task.FromResult(new NodeExecutionResult { Success = true, Output = new DataBatch(), BranchIndex = 1 });
        }
    }

    /// <summary>
    /// 分叉观察节点：仅捕获上下文引用并结束（无出边），用于验证非回边激活各自重置。
    /// </summary>
    private sealed class ForkObservingNode : INodeType
    {
        public List<IDictionary<string, object?>> Captured { get; } = [];

        public string TypeName => "obs";
        public string DisplayName => "Obs";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports { get; } =
        [
            new PortDefinition { Name = FlowConstants.PortNames.Input, Direction = PortDirection.Input, Type = PortType.Main },
            new PortDefinition { Name = FlowConstants.PortNames.Output, Direction = PortDirection.Output, Type = PortType.Main }
        ];
        public bool DefaultIsEntry => false;

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
        {
            Captured.Add(context.NodeContext);
            return Task.FromResult(new NodeExecutionResult { Success = true, Output = new DataBatch() });
        }
    }

    /// <summary>
    /// 入口源节点：输出单条数据，供下游观察节点消费。
    /// </summary>
    private sealed class SourceNode : INodeType
    {
        public string TypeName => "source";
        public string DisplayName => "Source";
        public string Category => "Test";
        public string Icon => "test";
        public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;
        public IReadOnlyList<PortDefinition> Ports { get; } =
        [
            new PortDefinition { Name = FlowConstants.PortNames.Input, Direction = PortDirection.Input, Type = PortType.Main },
            new PortDefinition { Name = FlowConstants.PortNames.Output, Direction = PortDirection.Output, Type = PortType.Main }
        ];
        public bool DefaultIsEntry => true;

        public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new NodeExecutionResult
            {
                Success = true,
                Output = new DataBatch { Items = [new DataItem { Data = 1, Success = true, SourceIndex = 0 }] }
            });
    }

    private sealed class CollectingSideEffects : IExecutionSideEffects
    {
        public Task PersistNodeRecordAsync(NodeExecutionRecord record, CancellationToken cancellationToken) => Task.CompletedTask;
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
