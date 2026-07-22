using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Events;

namespace FlowEngine.Core.Tests;

public class DomainEventsMoreTests
{
    [Fact]
    public void AuditEvent_Base_Properties_AreInitialized()
    {
        var evt = new WorkflowStartedEvent(Guid.NewGuid(), Guid.NewGuid());

        Assert.NotEqual(Guid.Empty, evt.EventId);
        Assert.True(evt.OccurredAt <= DateTime.UtcNow);
        Assert.Equal(AuditEventTypes.ExecutionStarted, evt.EventType);
        Assert.Equal("Execution", evt.ResourceType);
    }

    [Fact]
    public void AuditEventTypes_CriticalEvents_ContainsExpected()
    {
        Assert.Contains(AuditEventTypes.CredentialAccessed, AuditEventTypes.CriticalEvents);
        Assert.Contains(AuditEventTypes.CredentialDeleted, AuditEventTypes.CriticalEvents);
        Assert.Contains(AuditEventTypes.ExecutionCancelled, AuditEventTypes.CriticalEvents);
        Assert.Contains(AuditEventTypes.ExecutionDeleted, AuditEventTypes.CriticalEvents);
    }

    [Fact]
    public void NodeStartedEvent_Holds_Given_Values()
    {
        var executionId = Guid.NewGuid();
        var evt = new NodeStartedEvent(executionId, "node-1", 1);

        Assert.Equal(executionId, evt.ExecutionId);
        Assert.Equal("node-1", evt.NodeDefinitionId);
        Assert.Equal(1, evt.RunIndex);
        Assert.Equal(AuditEventTypes.NodeStarted, evt.EventType);
        Assert.Equal("Node", evt.ResourceType);
    }

    [Fact]
    public void NodeErrorEvent_Holds_Given_Values()
    {
        var executionId = Guid.NewGuid();
        var error = new NodeError { Code = "E1", Message = "msg" };

        var evt = new NodeErrorEvent(executionId, "node-1", 2, error);

        Assert.Equal(executionId, evt.ExecutionId);
        Assert.Equal("node-1", evt.NodeDefinitionId);
        Assert.Equal(2, evt.RunIndex);
        Assert.Equal("E1", evt.Error!.Code);
        Assert.Equal(AuditEventTypes.NodeError, evt.EventType);
    }

    [Fact]
    public void WorkflowFailedEvent_Holds_Given_Values()
    {
        var executionId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();
        var error = new NodeError { Code = "E1", Message = "msg" };

        var evt = new WorkflowFailedEvent(executionId, workflowDefinitionId, error);

        Assert.Equal(executionId, evt.ExecutionId);
        Assert.Equal(workflowDefinitionId, evt.WorkflowDefinitionId);
        Assert.Equal("E1", evt.Error!.Code);
        Assert.Equal(AuditEventTypes.ExecutionFailed, evt.EventType);
    }

    [Fact]
    public void WorkflowCancelledEvent_Holds_Given_Values()
    {
        var executionId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();

        var evt = new WorkflowCancelledEvent(executionId, workflowDefinitionId);

        Assert.Equal(executionId, evt.ExecutionId);
        Assert.Equal(workflowDefinitionId, evt.WorkflowDefinitionId);
        Assert.Equal(AuditEventTypes.ExecutionCancelled, evt.EventType);
    }

    [Fact]
    public void LlmTokenStreamEvent_Holds_Given_Values()
    {
        var executionId = Guid.NewGuid();

        var evt = new LlmTokenStreamEvent
        {
            ExecutionId = executionId,
            NodeDefinitionId = "node-1",
            RunIndex = 1,
            Delta = "hello",
            IsFinal = true
        };

        Assert.Equal(executionId, evt.ExecutionId);
        Assert.Equal("node-1", evt.NodeDefinitionId);
        Assert.Equal(1, evt.RunIndex);
        Assert.Equal("hello", evt.Delta);
        Assert.True(evt.IsFinal);
        Assert.Equal(AuditEventTypes.LlmTokenStream, evt.EventType);
    }

    [Fact]
    public void LlmTokenStreamEvent_Defaults_AreExpected()
    {
        var evt = new LlmTokenStreamEvent();

        Assert.NotEqual(Guid.Empty, evt.EventId);
        Assert.Equal(AuditEventTypes.LlmTokenStream, evt.EventType);
    }

    [Fact]
    public void WorkflowStartedEvent_DefaultTriggerPayload_IsNull()
    {
        var executionId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();

        var evt = new WorkflowStartedEvent(executionId, workflowDefinitionId);

        Assert.Null(evt.TriggerPayload);
    }

    [Fact]
    public void WorkflowCompletedEvent_DefaultStatus_IsCompleted()
    {
        var executionId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();

        var evt = new WorkflowCompletedEvent(executionId, workflowDefinitionId, ExecutionStatus.Completed);

        Assert.Equal(ExecutionStatus.Completed, evt.FinalStatus);
        Assert.Equal(AuditEventTypes.ExecutionCompleted, evt.EventType);
    }
}
