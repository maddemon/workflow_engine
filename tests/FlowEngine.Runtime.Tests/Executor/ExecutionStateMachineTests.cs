using FlowEngine.Core.Enums;
using FlowEngine.Runtime.Executor;

namespace FlowEngine.Runtime.Tests.Executor;

/// <summary>
/// <see cref="ExecutionStateMachine"/> 状态转换行为测试。
/// 覆盖合法转换与非法转换的静默忽略（与原 switch + if 守卫语义一致）。
/// </summary>
public sealed class ExecutionStateMachineTests
{
    [Fact]
    public void Constructor_DefaultsToPending()
    {
        var machine = new ExecutionStateMachine();
        Assert.Equal(ExecutionStatus.Pending, machine.Status);
    }

    [Fact]
    public void Constructor_WithInitialStatus_UsesGivenStatus()
    {
        var machine = new ExecutionStateMachine(ExecutionStatus.Running);
        Assert.Equal(ExecutionStatus.Running, machine.Status);
    }

    [Fact]
    public void Start_FromPending_TransitionsToRunning()
    {
        var machine = new ExecutionStateMachine();
        machine.Start();
        Assert.Equal(ExecutionStatus.Running, machine.Status);
    }

    [Fact]
    public void Complete_FromRunning_TransitionsToCompleted()
    {
        var machine = new ExecutionStateMachine();
        machine.Start();
        machine.Complete();
        Assert.Equal(ExecutionStatus.Completed, machine.Status);
    }

    [Fact]
    public void Fail_FromRunning_TransitionsToFailed()
    {
        var machine = new ExecutionStateMachine();
        machine.Start();
        machine.Fail();
        Assert.Equal(ExecutionStatus.Failed, machine.Status);
    }

    [Fact]
    public void Cancel_FromPending_TransitionsToCancelled()
    {
        var machine = new ExecutionStateMachine();
        machine.Cancel();
        Assert.Equal(ExecutionStatus.Cancelled, machine.Status);
    }

    [Fact]
    public void Cancel_FromRunning_TransitionsToCancelled()
    {
        var machine = new ExecutionStateMachine();
        machine.Start();
        machine.Cancel();
        Assert.Equal(ExecutionStatus.Cancelled, machine.Status);
    }

    [Fact]
    public void HappyPath_PendingToRunningToCompleted()
    {
        var machine = new ExecutionStateMachine();
        machine.Start();
        Assert.Equal(ExecutionStatus.Running, machine.Status);
        machine.Complete();
        Assert.Equal(ExecutionStatus.Completed, machine.Status);
    }

    [Fact]
    public void Start_WhenNotPending_IsIgnored()
    {
        var machine = new ExecutionStateMachine(ExecutionStatus.Running);
        machine.Start();
        Assert.Equal(ExecutionStatus.Running, machine.Status);
    }

    [Fact]
    public void Complete_WhenNotRunning_IsIgnored()
    {
        var machine = new ExecutionStateMachine();
        machine.Complete();
        Assert.Equal(ExecutionStatus.Pending, machine.Status);
    }

    [Fact]
    public void Fail_WhenNotRunning_IsIgnored()
    {
        var machine = new ExecutionStateMachine();
        machine.Fail();
        Assert.Equal(ExecutionStatus.Pending, machine.Status);
    }

    [Fact]
    public void Cancel_WhenTerminal_IsIgnored()
    {
        var machine = new ExecutionStateMachine();
        machine.Start();
        machine.Complete();
        machine.Cancel();
        Assert.Equal(ExecutionStatus.Completed, machine.Status);
    }

    [Fact]
    public void Complete_AfterCancel_IsIgnored()
    {
        var machine = new ExecutionStateMachine();
        machine.Start();
        machine.Cancel();
        machine.Complete();
        Assert.Equal(ExecutionStatus.Cancelled, machine.Status);
    }

    [Fact]
    public void DryRunComplete_FromRunning_TransitionsToDryRunCompleted()
    {
        var machine = new ExecutionStateMachine();
        machine.Start();
        machine.DryRunComplete();
        Assert.Equal(ExecutionStatus.DryRunCompleted, machine.Status);
    }

    [Fact]
    public void DryRunComplete_WhenNotRunning_IsIgnored()
    {
        var machine = new ExecutionStateMachine();
        machine.DryRunComplete();
        Assert.Equal(ExecutionStatus.Pending, machine.Status);
    }

    [Fact]
    public void Compensate_FromCompleted_TransitionsToCompensating()
    {
        var machine = new ExecutionStateMachine();
        machine.Start();
        machine.Complete();
        machine.Compensate();
        Assert.Equal(ExecutionStatus.Compensating, machine.Status);
    }

    [Fact]
    public void CompensationSucceed_FromCompensating_TransitionsToCompensated()
    {
        var machine = new ExecutionStateMachine();
        machine.Start();
        machine.Complete();
        machine.Compensate();
        machine.CompensationSucceed();
        Assert.Equal(ExecutionStatus.Compensated, machine.Status);
    }

    [Fact]
    public void CompensationFail_FromCompensating_TransitionsToCompensationFailed()
    {
        var machine = new ExecutionStateMachine();
        machine.Start();
        machine.Complete();
        machine.Compensate();
        machine.CompensationFail();
        Assert.Equal(ExecutionStatus.CompensationFailed, machine.Status);
    }

    [Fact]
    public void Compensate_WhenNotCompleted_IsIgnored()
    {
        var machine = new ExecutionStateMachine();
        machine.Start();
        machine.Compensate();
        Assert.Equal(ExecutionStatus.Running, machine.Status);
    }
}
