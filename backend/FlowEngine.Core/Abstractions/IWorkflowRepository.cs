using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 工作流仓储。
/// </summary>
public interface IWorkflowRepository
{
    /// <summary>
    /// 按 ID 获取工作流。
    /// </summary>
    /// <param name="id">工作流 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工作流或 null。</returns>
    Task<Workflow?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有工作流。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工作流集合。</returns>
    Task<IReadOnlyCollection<Workflow>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存工作流。
    /// </summary>
    /// <param name="workflow">工作流实例。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task SaveAsync(Workflow workflow, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除工作流。
    /// </summary>
    /// <param name="id">工作流 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 ID 与版本号获取工作流。
    /// </summary>
    /// <param name="id">工作流 ID。</param>
    /// <param name="version">版本号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工作流或 null。</returns>
    Task<Workflow?> GetByVersionAsync(Guid id, int version, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取工作流的所有历史版本号。
    /// </summary>
    /// <param name="id">工作流 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>版本号集合。</returns>
    Task<IReadOnlyCollection<int>> GetVersionsAsync(Guid id, CancellationToken cancellationToken = default);
}
