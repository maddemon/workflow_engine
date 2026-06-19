namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 执行队列工作项。
/// </summary>
/// <param name="ExecutionId">执行 ID。</param>
/// <param name="NodeInstanceId">节点实例 ID。</param>
/// <param name="Inputs">按端口名组织的输入数据。</param>
public sealed record NodeWorkItem(
    Guid ExecutionId,
    Guid NodeInstanceId,
    IReadOnlyDictionary<string, Core.Entities.DataBatch> Inputs);
