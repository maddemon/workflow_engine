using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.ValueObjects;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Registry;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Executor;

/// <summary>
/// <see cref="OutputRouter"/> 独立单测：验证单输入端口目标直接入队、多输入端口目标经等待区聚合，
/// 且入队时脉冲调度唤醒信号。行为须与从 <see cref="WorkflowSchedulerKernel"/> 抽离前完全一致。
/// </summary>
public sealed class OutputRouterTests
{
    private readonly INodeRegistry _nodeRegistry = new NodeRegistry(
        new INodeType[] { new PassThroughNode(), new MergeNode() },
        NullLogger<NodeRegistry>.Instance);

    private readonly OutputRouter _router = new(
        new NodeRegistry(
            new INodeType[] { new PassThroughNode(), new MergeNode() },
            NullLogger<NodeRegistry>.Instance),
        NullLogger<OutputRouter>.Instance);

    [Fact]
    public async Task RouteOutputsAsync_SingleInputPortTarget_EnqueuesTargetWorkItemAndPulses()
    {
        var source = CreateNode("a", "passThrough");
        var target = CreateNode("b", "passThrough");
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "w",
            CreatedBy = "t",
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
        var sourceType = _nodeRegistry.Get(source.TypeName);
        var result = new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch
            {
                Items = [new DataItem { Data = 42, Success = true, SourceIndex = 0 }]
            }
        };

        await _router.RouteOutputsAsync(source, sourceType, result, session, new NoopSideEffects(), CancellationToken.None);

        Assert.True(session.Queue.Reader.TryRead(out var item));
        Assert.Equal(target.Id, item.NodeInstanceId);
        Assert.True(item.Inputs.TryGetValue(FlowConstants.PortNames.Input, out var inputBatch));
        Assert.Single(inputBatch.Items);
        Assert.Equal(42, inputBatch.Items[0].Data!.GetValue<int>());
        // 入队后脉冲唤醒信号（CON-6）。
        Assert.Equal(1, session.SchedulerWake.CurrentCount);
    }

    [Fact]
    public async Task RouteOutputsAsync_MultiInputPortTarget_UsesWaitingAreaUntilReady()
    {
        var source = CreateNode("a", "passThrough");
        var target = CreateNode("m", "merge");
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "w",
            CreatedBy = "t",
            Nodes = [source, target],
            Connections =
            [
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = source.Id,
                    SourcePortName = FlowConstants.PortNames.Output,
                    TargetNodeId = target.Id,
                    TargetPortName = "a"
                }
            ]
        };
        var session = BuildSession(workflow);
        var sourceType = _nodeRegistry.Get(source.TypeName);
        var targetType = _nodeRegistry.Get(target.TypeName);
        var result = new NodeExecutionResult
        {
            Success = true,
            Output = new DataBatch
            {
                Items = [new DataItem { Data = "x", Success = true, SourceIndex = 0 }]
            }
        };

        await _router.RouteOutputsAsync(source, sourceType, result, session, new NoopSideEffects(), CancellationToken.None);

        // 仅 1/2 端口到达，未集齐 → 不入队。
        Assert.False(session.Queue.Reader.TryRead(out _));
        Assert.False(session.WaitingArea.IsEmpty);
        var inputPorts = OutputRouter.GetInputPortNames(targetType);
        Assert.False(session.WaitingArea.IsReady(session.Execution.Id, target.Id, inputPorts));
    }

    [Fact]
    public async Task RouteOutputsAsync_MultiOutputPortSource_RoutesEachPortToItsTarget()
    {
        // 复现 Filter 节点 bug：一个节点同时向 Kept / Discarded 两个输出端口分发数据，
        // OutputRouter 必须按各命名端口分别路由，否则连到 Kept/Discarded 的下游永远不被调度。
        var source = CreateNode("f", "passThrough");
        var kept = CreateNode("k", "passThrough");
        var discarded = CreateNode("d", "passThrough");
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = "w",
            CreatedBy = "t",
            Nodes = [source, kept, discarded],
            Connections =
            [
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = source.Id,
                    SourcePortName = FlowConstants.PortNames.Kept,
                    TargetNodeId = kept.Id,
                    TargetPortName = FlowConstants.PortNames.Input
                },
                new Connection
                {
                    Id = Guid.NewGuid(),
                    SourceNodeId = source.Id,
                    SourcePortName = FlowConstants.PortNames.Discarded,
                    TargetNodeId = discarded.Id,
                    TargetPortName = FlowConstants.PortNames.Input
                }
            ]
        };
        var session = BuildSession(workflow);
        var sourceType = _nodeRegistry.Get(source.TypeName);
        var result = new NodeExecutionResult
        {
            Success = true,
            PortOutputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
            {
                [FlowConstants.PortNames.Kept] = new DataBatch
                {
                    Items = [new DataItem { Data = 1, Success = true, SourceIndex = 0 }]
                },
                [FlowConstants.PortNames.Discarded] = new DataBatch
                {
                    Items = [new DataItem { Data = 2, Success = true, SourceIndex = 0 }]
                }
            }
        };

        await _router.RouteOutputsAsync(source, sourceType, result, session, new NoopSideEffects(), CancellationToken.None);

        var enqueued = new List<NodeWorkItem>();
        while (session.Queue.Reader.TryRead(out var it))
        {
            enqueued.Add(it);
        }

        Assert.Equal(2, enqueued.Count);
        var keptItem = enqueued.Single(i => i.NodeInstanceId == kept.Id);
        var discardedItem = enqueued.Single(i => i.NodeInstanceId == discarded.Id);
        Assert.Equal(1, keptItem.Inputs[FlowConstants.PortNames.Input].Items[0].Data!.GetValue<int>());
        Assert.Equal(2, discardedItem.Inputs[FlowConstants.PortNames.Input].Items[0].Data!.GetValue<int>());
    }

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

    private static NodeDefinition CreateNode(string name, string typeName)
    {
        return new NodeDefinition
        {
            Id = name,
            Name = name,
            TypeName = typeName
        };
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
