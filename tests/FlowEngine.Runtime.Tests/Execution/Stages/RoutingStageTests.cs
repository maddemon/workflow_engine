using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Execution.Pipeline;
using FlowEngine.Runtime.Execution.Stages;
using FlowEngine.Runtime.Registry;
using FlowEngine.Runtime.Tests.Executor;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Execution.Stages;

/// <summary>
/// <see cref="RoutingStage"/> 单元测试：验证存在路由结果时调用 <see cref="OutputRouter.RouteOutputsAsync"/>
/// 将输出分发至下游（真实路由器 + 真实队列断言），以及无路由结果时跳过路由且仍续链。
/// </summary>
public sealed class RoutingStageTests
{
    private static readonly INodeRegistry Registry = new NodeRegistry(
        new INodeType[] { new PassThroughNode() }, NullLogger<NodeRegistry>.Instance);

    private static readonly OutputRouter Router = new(Registry, NullLogger<OutputRouter>.Instance);

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
    public async Task RunAsync_RoutingResultPresent_InvokesRouteOutputsAsync_AndEnqueuesTarget()
    {
        var source = CreateNode("a");
        var target = CreateNode("b");
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "w",
            Nodes = [source, target],
            Connections =
            [
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = source.Id,
                    SourcePortName = FlowConstants.PortNames.Output,
                    TargetNodeId = target.Id,
                    TargetPortName = FlowConstants.PortNames.Input
                }
            ]
        };
        var session = BuildSession(workflow);
        var sourceType = Registry.Get(source.TypeName);
        var result = new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch { Items = [new DataItem { Data = 42, Success = true, SourceIndex = 0 }] }
        };

        var context = new NodePipelineContext(
            new NodeWorkItem(Guid.NewGuid(), source.Id, new Dictionary<string, DataBatch>()), session, new NoopSideEffects())
        {
            NodeDefinition = source,
            NodeType = sourceType,
            RoutingResult = result
        };

        var nextCalled = false;
        await new RoutingStage(Router)
            .RunAsync(context, () => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        Assert.True(nextCalled);
        Assert.True(session.Queue.Reader.TryRead(out var item));
        Assert.Equal(target.Id, item.NodeInstanceId);
    }

    [Fact]
    public async Task RunAsync_RoutingResultNull_SkipsRouting_AndStillCallsNext()
    {
        var source = CreateNode("a");
        var target = CreateNode("b");
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "w",
            Nodes = [source, target],
            Connections =
            [
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = source.Id,
                    SourcePortName = FlowConstants.PortNames.Output,
                    TargetNodeId = target.Id,
                    TargetPortName = FlowConstants.PortNames.Input
                }
            ]
        };
        var session = BuildSession(workflow);
        var sourceType = Registry.Get(source.TypeName);

        var context = new NodePipelineContext(
            new NodeWorkItem(Guid.NewGuid(), source.Id, new Dictionary<string, DataBatch>()), session, new NoopSideEffects())
        {
            NodeDefinition = source,
            NodeType = sourceType,
            RoutingResult = null
        };

        var nextCalled = false;
        await new RoutingStage(Router)
            .RunAsync(context, () => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        Assert.True(nextCalled);
        // 无路由结果 → 不应向队列入队任何下游工作项。
        Assert.False(session.Queue.Reader.TryRead(out _));
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
