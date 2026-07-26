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

    /// <summary>
    /// 按 ID 或名称解析凭据（默认实现，复刻 <see cref="Entities.NodeExecutionContextExtensions.ResolveCredentialAsync"/>
    /// 的 Guid/名称分派逻辑）。空标识符或解析失败均返回 null，调用方需自行判空。
    /// 注意：此默认实现无法访问上下文日志，故省略审计日志（与经上下文解析的差异仅在于此）。
    /// </summary>
    /// <param name="idOrName">凭据 ID（Guid 形式）或名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>凭据值；找不到或失败时返回 null。</returns>
    async Task<CredentialValue?> ResolveAsync(string? idOrName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(idOrName)) return null;
        try
        {
            if (Guid.TryParse(idOrName, out var id))
            {
                return await GetCredentialAsync(id, cancellationToken).ConfigureAwait(false);
            }

            return await GetCredentialByNameAsync(idOrName, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }
}
