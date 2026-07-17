namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 执行状态机的触发器（引发状态转换的事件）。
/// </summary>
public enum ExecutionTrigger
{
    /// <summary>
    /// 开始执行。
    /// </summary>
    Start,

    /// <summary>
    /// 执行完成。
    /// </summary>
    Complete,

    /// <summary>
    /// 执行失败。
    /// </summary>
    Fail,

    /// <summary>
    /// 取消执行。
    /// </summary>
    Cancel,

    /// <summary>
    /// 开始补偿。
    /// </summary>
    Compensate,

    /// <summary>
    /// 补偿完成。
    /// </summary>
    CompensationSucceed,

    /// <summary>
    /// 补偿失败。
    /// </summary>
    CompensationFail,

    /// <summary>
    /// 模拟运行完成。
    /// </summary>
    DryRunComplete
}
