using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 凭据访问器。
/// </summary>
public interface ICredentialAccessor
{
    /// <summary>
    /// 获取指定凭据的值。
    /// </summary>
    /// <param name="credentialId">凭据 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>凭据值。</returns>
    Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按名称获取凭据值（可选实现，供 dry-run 等临时凭据场景使用）。
    /// </summary>
    /// <param name="name">凭据名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>凭据值；未找到时返回 null。</returns>
    Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
        => Task.FromResult<CredentialValue?>(null);
}
