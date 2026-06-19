using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;

namespace FlowEngine.Core.Tests;

public class DomainEventsTests
{
    [Fact]
    public void WorkflowStartedEvent_Holds_Given_Values()
    {
        var executionId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();

        var evt = new WorkflowStartedEvent(executionId, workflowDefinitionId, new { name = "test" });

        Assert.NotEqual(Guid.Empty, evt.EventId);
        Assert.Equal(executionId, evt.ExecutionId);
        Assert.Equal(workflowDefinitionId, evt.WorkflowDefinitionId);
        Assert.NotNull(evt.TriggerPayload);
    }

    [Fact]
    public void NodeExecutedEvent_Holds_Given_Values()
    {
        var executionId = Guid.NewGuid();
        var nodeDefinitionId = Guid.NewGuid();
        var result = new NodeExecutionResult { Success = true };

        var evt = new NodeExecutedEvent(executionId, nodeDefinitionId, 2, result);

        Assert.Equal(executionId, evt.ExecutionId);
        Assert.Equal(nodeDefinitionId, evt.NodeDefinitionId);
        Assert.Equal(2, evt.RunIndex);
        Assert.True(evt.Result.Success);
    }

    [Fact]
    public void WorkflowCompletedEvent_Holds_Given_Values()
    {
        var executionId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();

        var evt = new WorkflowCompletedEvent(executionId, workflowDefinitionId, ExecutionStatus.Completed);

        Assert.Equal(executionId, evt.ExecutionId);
        Assert.Equal(workflowDefinitionId, evt.WorkflowDefinitionId);
        Assert.Equal(ExecutionStatus.Completed, evt.FinalStatus);
    }

    [Fact]
    public void CredentialAccessedEvent_Holds_Given_Values()
    {
        var credentialId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var nodeDefinitionId = Guid.NewGuid();

        var evt = new CredentialAccessedEvent(credentialId, executionId, nodeDefinitionId, "read");

        Assert.Equal(credentialId, evt.CredentialId);
        Assert.Equal(executionId, evt.ExecutionId);
        Assert.Equal(nodeDefinitionId, evt.NodeDefinitionId);
        Assert.Equal("read", evt.AccessType);
    }
}
