using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Runtime.Executor;

/// <summary>
/// 执行副作用回调：将 <see cref="WorkflowSchedulerKernel"/> 的纯内存调度逻辑与
/// 持久化、事件发布等外部副作用解耦。
/// <list type="bullet">
///   <item>普通执行外壳实现为落库（DbContext）+ 事件总线发布。</item>
///   <item>Dry-Run 外壳实现为无操作（不落库、不发布事件）。</item>
/// </list>
/// </summary>
public interface IExecutionSideEffects
{
    /// <summary>
    /// 持久化单个节点执行记录（落库）。调用前内核已将其加入 <see cref="ExecutionRecord.NodeRecords"/>。
    /// </summary>
    /// <param name="record">节点执行记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PersistNodeRecordAsync(NodeExecutionRecord record, CancellationToken cancellationToken);

    /// <summary>
    /// 在节点失败（终止策略）时持久化执行失败状态。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PersistFailedStateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 持久化执行整体终态（完成/失败/取消）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PersistExecutionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 发布节点开始执行事件。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="nodeId">节点定义 ID。</param>
    /// <param name="runIndex">运行索引。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PublishNodeStartedAsync(Guid executionId, Guid nodeId, int runIndex, CancellationToken cancellationToken);

    /// <summary>
    /// 发布执行完成事件。
    /// </summary>
    /// <param name="status">终态状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PublishCompletedAsync(ExecutionStatus status, CancellationToken cancellationToken);

    /// <summary>
    /// 创建 LLM 流式回调，用于节点执行过程中的 token 推送。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="nodeId">节点定义 ID。</param>
    /// <param name="runIndex">运行索引。</param>
    /// <returns>流式回调委托；无事件总线时返回无操作回调。</returns>
    Func<LlmStreamChunk, CancellationToken, Task> CreateLlmStreamCallback(Guid executionId, Guid nodeId, int runIndex);
}
