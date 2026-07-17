using FlowEngine.Core.Enums;
using Stateless;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 执行状态机。
/// 基于 Stateless 实现，合法转换见各状态的 <c>Permit</c> 配置；
/// 与重构前一致，非法转换（当前状态下无对应 Permit）被静默忽略。
/// </summary>
public sealed class ExecutionStateMachine
{
    private readonly StateMachine<ExecutionStatus, ExecutionTrigger> _machine;

    /// <summary>
    /// 当前状态。
    /// </summary>
    public ExecutionStatus Status => _machine.State;

    /// <summary>
    /// 初始化状态机。
    /// </summary>
    /// <param name="initialStatus">初始状态。</param>
    public ExecutionStateMachine(ExecutionStatus initialStatus = ExecutionStatus.Pending)
    {
        _machine = new StateMachine<ExecutionStatus, ExecutionTrigger>(initialStatus);

        _machine.Configure(ExecutionStatus.Pending)
            .Permit(ExecutionTrigger.Start, ExecutionStatus.Running)
            .Permit(ExecutionTrigger.Cancel, ExecutionStatus.Cancelled);

        _machine.Configure(ExecutionStatus.Running)
            .Permit(ExecutionTrigger.Complete, ExecutionStatus.Completed)
            .Permit(ExecutionTrigger.Fail, ExecutionStatus.Failed)
            .Permit(ExecutionTrigger.Cancel, ExecutionStatus.Cancelled)
            .Permit(ExecutionTrigger.DryRunComplete, ExecutionStatus.DryRunCompleted);

        _machine.Configure(ExecutionStatus.Completed)
            .Permit(ExecutionTrigger.Compensate, ExecutionStatus.Compensating);

        _machine.Configure(ExecutionStatus.Compensating)
            .Permit(ExecutionTrigger.CompensationSucceed, ExecutionStatus.Compensated)
            .Permit(ExecutionTrigger.CompensationFail, ExecutionStatus.CompensationFailed);
    }

    /// <summary>
    /// 标记为执行中（Pending→Running）。
    /// </summary>
    public void Start() => FireIfPermitted(ExecutionTrigger.Start);

    /// <summary>
    /// 标记为已完成（Running→Completed）。
    /// </summary>
    public void Complete() => FireIfPermitted(ExecutionTrigger.Complete);

    /// <summary>
    /// 标记为失败（Running→Failed）。
    /// </summary>
    public void Fail() => FireIfPermitted(ExecutionTrigger.Fail);

    /// <summary>
    /// 标记为已取消（Pending/Running→Cancelled）。
    /// </summary>
    public void Cancel() => FireIfPermitted(ExecutionTrigger.Cancel);

    /// <summary>
    /// 开始补偿（Completed→Compensating）。
    /// </summary>
    public void Compensate() => FireIfPermitted(ExecutionTrigger.Compensate);

    /// <summary>
    /// 标记补偿完成（Compensating→Compensated）。
    /// </summary>
    public void CompensationSucceed() => FireIfPermitted(ExecutionTrigger.CompensationSucceed);

    /// <summary>
    /// 标记补偿失败（Compensating→CompensationFailed）。
    /// </summary>
    public void CompensationFail() => FireIfPermitted(ExecutionTrigger.CompensationFail);

    /// <summary>
    /// 标记模拟运行完成（Running→DryRunCompleted）。
    /// </summary>
    public void DryRunComplete() => FireIfPermitted(ExecutionTrigger.DryRunComplete);

    /// <summary>
    /// 仅当当前状态允许该触发器时才触发，否则静默忽略（等价于重构前的 if 守卫语义）。
    /// </summary>
    private void FireIfPermitted(ExecutionTrigger trigger)
    {
        if (_machine.CanFire(trigger))
        {
            _machine.Fire(trigger);
        }
    }
}
