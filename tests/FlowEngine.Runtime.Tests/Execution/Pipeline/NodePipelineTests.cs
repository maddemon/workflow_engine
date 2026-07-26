using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Execution.Pipeline;
using Xunit;

namespace FlowEngine.Runtime.Tests.Execution.Pipeline;

public sealed class NodePipelineTests
{
    [Fact]
    public async Task RunAsync_AllStagesRunInOrder_WhenNoShortCircuit()
    {
        var log = new List<string>();
        var stages = new IExecutionStage[]
        {
            new RecordingStage("s1", log),
            new RecordingStage("s2", log),
            new RecordingStage("s3", log),
        };

        var pipeline = new NodePipeline(stages);
        var (item, session, sideEffects) = BuildHarness();

        var result = await pipeline.RunAsync(item, session, sideEffects, CancellationToken.None);

        Assert.Equal(new[] { "s1", "s2", "s3" }, log);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task RunAsync_ShortCircuit_SkipsIntermediateStages_AndRunsTerminal()
    {
        var log = new List<string>();
        var stages = new IExecutionStage[]
        {
            new FailingStage(log, "A"),
            new RecordingStage("B", log),
            new RecordingStage("C", log), // 末端（持久化）阶段
        };

        var pipeline = new NodePipeline(stages);
        var (item, session, sideEffects) = BuildHarness();

        var result = await pipeline.RunAsync(item, session, sideEffects, CancellationToken.None);

        // 中间阶段 B 被跳过，末端阶段 C 仍执行
        Assert.DoesNotContain("B", log);
        Assert.Contains("C", log);
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal("X", result.Error!.Code);
        Assert.Equal("y", result.Error.Message);
    }

    [Fact]
    public async Task RunAsync_TerminalStageSetsResult_NoExtraJump()
    {
        var log = new List<string>();
        var stages = new IExecutionStage[]
        {
            new FailingStage(log, "only"), // 同时是末端阶段
        };

        var pipeline = new NodePipeline(stages);
        var (item, session, sideEffects) = BuildHarness();

        var result = await pipeline.RunAsync(item, session, sideEffects, CancellationToken.None);

        Assert.Single(log);
        Assert.Contains("only", log);
        Assert.NotNull(result.Error);
        Assert.Equal("X", result.Error!.Code);
    }

    [Fact]
    public void NodePipelineContext_CanBeConstructed()
    {
        var (item, session, sideEffects) = BuildHarness();

        var context = new NodePipelineContext(item, session, sideEffects);

        Assert.Same(item, context.Item);
        Assert.Same(session, context.Session);
        Assert.Same(sideEffects, context.SideEffects);
        Assert.NotNull(context.ValidationErrors);
        Assert.Empty(context.ValidationErrors);
    }

    // ---- 测试替身 ----

    private sealed class RecordingStage(string name, List<string> log) : IExecutionStage
    {
        public async Task RunAsync(NodePipelineContext context, Func<Task> next, CancellationToken ct)
        {
            log.Add(name);
            await next();
        }
    }

    private sealed class FailingStage(List<string> log, string name) : IExecutionStage
    {
        public Task RunAsync(NodePipelineContext context, Func<Task> next, CancellationToken ct)
        {
            log.Add(name);
            context.Result = new NodeExecutionResult
            {
                Success = false,
                Error = new NodeError { Code = "X", Message = "y" }
            };
            // 故意不调用 next，触发短路
            return Task.CompletedTask;
        }
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

    private static (NodeWorkItem, ExecutionSession, IExecutionSideEffects) BuildHarness()
    {
        var workflow = new Workflow();
        var record = new ExecutionRecord
        {
            Id = Guid.NewGuid(),
            WorkflowDefinitionId = workflow.Id,
            StartedAt = DateTime.UtcNow,
            Status = ExecutionStatus.Pending,
            NodeRecords = []
        };
        var session = new ExecutionSession(workflow, record, record.Id);
        var item = new NodeWorkItem(Guid.NewGuid(), "node1", new Dictionary<string, DataBatch>());
        return (item, session, new NoopSideEffects());
    }
}
