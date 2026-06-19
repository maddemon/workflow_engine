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
}
