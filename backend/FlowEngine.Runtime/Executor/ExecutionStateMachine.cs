using FlowEngine.Core.Enums;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 执行状态机。
/// </summary>
public sealed class ExecutionStateMachine
{
    /// <summary>
    /// 当前状态。
    /// </summary>
    public ExecutionStatus Status { get; private set; }

    /// <summary>
    /// 初始化状态机。
    /// </summary>
    /// <param name="initialStatus">初始状态。</param>
    public ExecutionStateMachine(ExecutionStatus initialStatus = ExecutionStatus.Pending)
    {
        Status = initialStatus;
    }

    /// <summary>
    /// 标记为执行中。
    /// </summary>
    public void Start()
    {
        if (Status == ExecutionStatus.Pending)
        {
            Status = ExecutionStatus.Running;
        }
    }

    /// <summary>
    /// 标记为已完成。
    /// </summary>
    public void Complete()
    {
        if (Status == ExecutionStatus.Running)
        {
            Status = ExecutionStatus.Completed;
        }
    }

    /// <summary>
    /// 标记为失败。
    /// </summary>
    public void Fail()
    {
        if (Status == ExecutionStatus.Running)
        {
            Status = ExecutionStatus.Failed;
        }
    }

    /// <summary>
    /// 标记为已取消。
    /// </summary>
    public void Cancel()
    {
        if (Status is ExecutionStatus.Pending or ExecutionStatus.Running)
        {
            Status = ExecutionStatus.Cancelled;
        }
    }
}
