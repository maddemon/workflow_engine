using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 凭据仓储。
/// </summary>
public interface ICredentialRepository
{
    /// <summary>
    /// 按 ID 获取凭据。
    /// </summary>
    /// <param name="id">凭据 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>凭据或 null。</returns>
    Task<Credential?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有凭据。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>凭据集合。</returns>
    Task<IReadOnlyCollection<Credential>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存凭据。
    /// </summary>
    /// <param name="credential">凭据实例。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task SaveAsync(Credential credential, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除凭据。
    /// </summary>
    /// <param name="id">凭据 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新凭据。
    /// </summary>
    /// <param name="credential">凭据实例。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task UpdateAsync(Credential credential, CancellationToken cancellationToken = default);
}
