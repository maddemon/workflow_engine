using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 执行记录存储。
/// </summary>
public interface IExecutionStore
{
    /// <summary>
    /// 按 ID 获取执行记录。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行记录或 null。</returns>
    Task<ExecutionRecord?> GetByIdAsync(Guid executionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按工作流定义 ID 获取执行记录集合。
    /// </summary>
    /// <param name="workflowDefinitionId">工作流定义 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行记录集合。</returns>
    Task<IReadOnlyCollection<ExecutionRecord>> GetByWorkflowDefinitionIdAsync(
        Guid workflowDefinitionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按状态获取执行记录集合。
    /// </summary>
    /// <param name="status">执行状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行记录集合。</returns>
    Task<IReadOnlyCollection<ExecutionRecord>> GetByStatusAsync(
        ExecutionStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存执行记录。
    /// </summary>
    /// <param name="executionRecord">执行记录实例。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task SaveAsync(ExecutionRecord executionRecord, CancellationToken cancellationToken = default);

    /// <summary>
    /// 追加节点执行记录。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="nodeRecord">节点执行记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task AddNodeRecordAsync(Guid executionId, NodeExecutionRecord nodeRecord, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新执行状态。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="status">执行状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task UpdateStatusAsync(Guid executionId, ExecutionStatus status, CancellationToken cancellationToken = default);
}

