namespace FlowEngine.Runtime.Credentials;

/// <summary>
/// OAuth2 令牌服务。
/// </summary>
public interface IOAuth2TokenService
{
    /// <summary>
    /// 从令牌端点获取访问令牌。
    /// </summary>
    Task<OAuth2TokenResponse> GetTokenAsync(OAuth2TokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按缓存键获取或刷新令牌，支持内存缓存与过期前刷新。
    /// </summary>
    Task<OAuth2TokenResponse> GetOrRefreshTokenAsync(string cacheKey, OAuth2TokenRequest request, CancellationToken cancellationToken = default);
}
