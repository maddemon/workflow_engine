using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Runtime.Executor;
using Xunit;

namespace FlowEngine.Runtime.Tests.Executor;

/// <summary>
/// 回边检测（<see cref="CycleDetector"/>，经 <see cref="ExecutionSession.FeedbackEdgeKeys"/> 暴露）单元测试。
/// 回边用于区分「环路回边激活」（复用节点上下文）与「新上游输入」（重置上下文），见 Task 9。
/// </summary>
public sealed class CycleDetectorTests
{
    [Fact]
    public void ExecutionSession_FeedbackEdgeKeys_DetectsBackEdgeInLoop()
    {
        var workflow = new Workflow
        {
            Nodes =
            [
                new NodeDefinition { Id = "loop", TypeName = "loop", Name = "loop" },
                new NodeDefinition { Id = "process", TypeName = "process", Name = "process" }
            ],
            Connections =
            [
                new Connection { Id = Guid.NewGuid(), SourceNodeId = "loop", SourcePortName = "loop", TargetNodeId = "process", TargetPortName = "input" },
                new Connection { Id = Guid.NewGuid(), SourceNodeId = "process", SourcePortName = "output", TargetNodeId = "loop", TargetPortName = "input" }
            ]
        };

        var session = new ExecutionSession(workflow, new ExecutionRecord { Id = Guid.NewGuid(), WorkflowDefinitionId = workflow.Id }, Guid.NewGuid());

        // process → loop 是回边（环路继续）。
        Assert.Contains(("process", "output", "loop", "input"), session.FeedbackEdgeKeys);
        // loop → process 是正向边，非回边。
        Assert.DoesNotContain(("loop", "loop", "process", "input"), session.FeedbackEdgeKeys);
    }

    [Fact]
    public void ExecutionSession_FeedbackEdgeKeys_NoCycle_ReturnsEmpty()
    {
        var workflow = new Workflow
        {
            Nodes =
            [
                new NodeDefinition { Id = "a", TypeName = "x", Name = "a" },
                new NodeDefinition { Id = "b", TypeName = "x", Name = "b" },
                new NodeDefinition { Id = "c", TypeName = "x", Name = "c" }
            ],
            Connections =
            [
                new Connection { Id = Guid.NewGuid(), SourceNodeId = "a", SourcePortName = "output", TargetNodeId = "b", TargetPortName = "input" },
                new Connection { Id = Guid.NewGuid(), SourceNodeId = "b", SourcePortName = "output", TargetNodeId = "c", TargetPortName = "input" }
            ]
        };

        var session = new ExecutionSession(workflow, new ExecutionRecord { Id = Guid.NewGuid(), WorkflowDefinitionId = workflow.Id }, Guid.NewGuid());

        Assert.Empty(session.FeedbackEdgeKeys);
    }

    [Fact]
    public void ExecutionSession_FeedbackEdgeKeys_SelfLoop_Detected()
    {
        var workflow = new Workflow
        {
            Nodes = [new NodeDefinition { Id = "a", TypeName = "x", Name = "a" }],
            Connections =
            [
                new Connection { Id = Guid.NewGuid(), SourceNodeId = "a", SourcePortName = "output", TargetNodeId = "a", TargetPortName = "input" }
            ]
        };

        var session = new ExecutionSession(workflow, new ExecutionRecord { Id = Guid.NewGuid(), WorkflowDefinitionId = workflow.Id }, Guid.NewGuid());

        Assert.Contains(("a", "output", "a", "input"), session.FeedbackEdgeKeys);
    }
}
