namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 执行队列工作项。
/// </summary>
/// <param name="ExecutionId">执行 ID。</param>
/// <param name="NodeInstanceId">节点实例 ID。</param>
/// <param name="Inputs">按端口名组织的输入数据。</param>
/// <param name="IsFeedbackActivation">该次激活是否来自环路回边（由 <see cref="WorkflowSchedulerKernel"/> 按回边集合标记）。回边激活复用节点上下文，非回边激活（新上游输入）触发上下文重置。</param>
public sealed record NodeWorkItem(
    Guid ExecutionId,
    string NodeInstanceId,
    IReadOnlyDictionary<string, Core.Entities.DataBatch> Inputs,
    bool IsFeedbackActivation = false);
