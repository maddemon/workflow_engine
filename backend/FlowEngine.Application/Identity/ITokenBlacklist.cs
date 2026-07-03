namespace FlowEngine.Application.Identity;

/// <summary>
/// Token 黑名单，用于登出后使已签发 JWT 失效。
/// </summary>
public interface ITokenBlacklist
{
    /// <summary>
    /// 将指定 JTI 加入黑名单。
    /// </summary>
    /// <param name="jti">Token 唯一标识。</param>
    /// <param name="expiresAt">Token 过期时间，用于设置缓存过期时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AddAsync(string jti, DateTime expiresAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查指定 JTI 是否在黑名单中。
    /// </summary>
    /// <param name="jti">Token 唯一标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否在黑名单中。</returns>
    Task<bool> IsBlacklistedAsync(string jti, CancellationToken cancellationToken = default);
}
