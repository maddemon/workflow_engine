using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Execution.Pipeline;
using FlowEngine.Runtime.Execution.Stages;
using FlowEngine.Runtime.Registry;
using FlowEngine.Runtime.Tests.Executor;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Execution.Stages;

/// <summary>
/// <see cref="InitializeStage"/> 单元测试：覆盖节点缺失短路、节点存在填充上下文并续链、
/// 以及环路失控保护（环路上限触发 → ShouldTerminateWorkflow 与失败结果）。
/// </summary>
public sealed class InitializeStageTests
{
    private static readonly INodeRegistry Registry = new NodeRegistry(
        new INodeType[] { new PassThroughNode() }, NullLogger<NodeRegistry>.Instance);

    private static NodeDefinition CreateNode(string name) => new() { Id = name, Name = name, TypeName = "passThrough" };

    private static ExecutionSession BuildSession(Workflow workflow)
    {
        var record = new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Pending,
            NodeRecords = []
        };
        return new ExecutionSession(workflow, record, record.Id);
    }

    [Fact]
    public async Task RunAsync_NodeMissing_FillsNothing_AndDoesNotCallNext()
    {
        var node = CreateNode("a");
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "w", Nodes = [node] };
        var session = BuildSession(workflow);
        var item = new NodeWorkItem(Guid.NewGuid(), "missing", new Dictionary<string, DataBatch>());
        var context = new NodePipelineContext(item, session, new NoopSideEffects());

        var nextCalled = false;
        await new InitializeStage(Registry, new EngineDefaultsOptions())
            .RunAsync(context, () => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        Assert.False(nextCalled);
        Assert.Null(context.NodeDefinition);
        Assert.Null(context.NodeType);
        Assert.False(context.ShouldTerminateWorkflow);
    }

    [Fact]
    public async Task RunAsync_NodePresent_PopulatesContext_AndCallsNext()
    {
        var node = CreateNode("a");
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "w", Nodes = [node] };
        var session = BuildSession(workflow);
        var item = new NodeWorkItem(Guid.NewGuid(), "a", new Dictionary<string, DataBatch>());
        var context = new NodePipelineContext(item, session, new NoopSideEffects());

        var nextCalled = false;
        await new InitializeStage(Registry, new EngineDefaultsOptions())
            .RunAsync(context, () => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        Assert.True(nextCalled);
        Assert.Same(node, context.NodeDefinition);
        Assert.NotNull(context.NodeType);
        Assert.Equal(1, context.RunCount);
        Assert.Equal(ExecutionMode.OnceForAll, context.ExecutionMode);
        Assert.NotNull(context.NodeContext);
        Assert.False(context.ShouldTerminateWorkflow);
    }

    [Fact]
    public async Task RunAsync_FeedbackActivationExceedsCycleLimit_SetsShouldTerminateWorkflow()
    {
        var node = CreateNode("a");
        var workflow = new Workflow { Id = Guid.NewGuid(), Name = "w", Nodes = [node] };
        var session = BuildSession(workflow);
        // 预置反馈计数为上限值，本次回边激活后 +1 超过上限。
        session.FeedbackActivationCounts[node.Id] = 1;

        var item = new NodeWorkItem(Guid.NewGuid(), "a", new Dictionary<string, DataBatch>(), IsFeedbackActivation: true);
        var context = new NodePipelineContext(item, session, new NoopSideEffects());

        var nextCalled = false;
        await new InitializeStage(Registry, new EngineDefaultsOptions { MaxCycleIterations = 1 })
            .RunAsync(context, () => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        Assert.True(context.ShouldTerminateWorkflow);
        Assert.NotNull(context.Result);
        Assert.False(context.Result!.Success);
        Assert.Equal(ExecutionStatus.Failed, session.Execution.Status);
        Assert.False(nextCalled);
    }

    private sealed class NoopSideEffects : IExecutionSideEffects
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
}
