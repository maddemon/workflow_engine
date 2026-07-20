using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Tests;

public class WorkflowValidatorTests
{
    [Fact]
    public void EnsureNonEmpty_NullWorkflow_ReturnsError()
    {
        var error = WorkflowValidator.EnsureNonEmpty(null);

        Assert.NotNull(error);
        Assert.Contains("null", error);
    }

    [Fact]
    public void EnsureNonEmpty_EmptyNodes_ReturnsError()
    {
        var workflow = new Workflow { Nodes = [] };

        var error = WorkflowValidator.EnsureNonEmpty(workflow);

        Assert.NotNull(error);
        Assert.Contains("no nodes", error);
    }

    [Fact]
    public void EnsureNonEmpty_NullNodes_ReturnsError()
    {
        var workflow = new Workflow { Nodes = null! };

        var error = WorkflowValidator.EnsureNonEmpty(workflow);

        Assert.NotNull(error);
        Assert.Contains("no nodes", error);
    }

    [Fact]
    public void EnsureNonEmpty_ValidWorkflow_ReturnsNull()
    {
        var workflow = new Workflow { Nodes = [new NodeDefinition { Id = "node-1" }] };

        var error = WorkflowValidator.EnsureNonEmpty(workflow);

        Assert.Null(error);
    }

    [Fact]
    public void EnsureNonEmpty_WorkflowWithConnectionsAndNodes_ReturnsNull()
    {
        var workflow = new Workflow
        {
            Nodes =
            [
                new NodeDefinition { Id = "a", TypeName = "manualTrigger" },
                new NodeDefinition { Id = "b", TypeName = "set" },
            ],
            Connections =
            [
                new Connection { Id = Guid.NewGuid(), SourceNodeId = "a", TargetNodeId = "b" },
            ],
        };

        var error = WorkflowValidator.EnsureNonEmpty(workflow);

        Assert.Null(error);
    }

    [Fact]
    public void EnsureNonEmpty_WorkflowWithMixedNodeTypes_ReturnsNull()
    {
        var workflow = new Workflow
        {
            Nodes =
            [
                new NodeDefinition { Id = "trigger", TypeName = "manualTrigger" },
                new NodeDefinition { Id = "action", TypeName = "set" },
            ],
        };

        var error = WorkflowValidator.EnsureNonEmpty(workflow);

        Assert.Null(error);
    }
}
