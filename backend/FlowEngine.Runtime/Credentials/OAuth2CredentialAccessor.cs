using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;

namespace FlowEngine.Runtime.Credentials;

/// <summary>
/// 凭据访问器装饰器：对 oauth2 类型凭据自动获取/刷新令牌，
/// 并将 accessToken/tokenType/expiresIn/expiresAt 注入内存中的 <see cref="CredentialValue.Fields"/>。
/// </summary>
public sealed class OAuth2CredentialAccessor : ICredentialAccessor
{
    private readonly ICredentialAccessor _inner;
    private readonly IOAuth2TokenService _tokenService;

    /// <summary>
    /// 初始化 OAuth2 凭据访问器装饰器。
    /// </summary>
    public OAuth2CredentialAccessor(ICredentialAccessor inner, IOAuth2TokenService tokenService)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
    }

    /// <inheritdoc />
    public async Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
    {
        var credential = await _inner.GetCredentialAsync(credentialId, cancellationToken).ConfigureAwait(false);
        return await EnrichIfNeededAsync(credential, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var credential = await _inner.GetCredentialByNameAsync(name, cancellationToken).ConfigureAwait(false);
        if (credential is null)
        {
            return null;
        }

        return await EnrichIfNeededAsync(credential, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CredentialValue> EnrichIfNeededAsync(CredentialValue credential, CancellationToken cancellationToken)
    {
        if (!string.Equals(credential.Type, "oauth2", StringComparison.OrdinalIgnoreCase))
        {
            return credential;
        }

        var fields = credential.Fields;
        if (!fields.TryGetValue("tokenUrl", out var tokenUrl) ||
            string.IsNullOrWhiteSpace(tokenUrl))
        {
            return credential;
        }

        fields.TryGetValue("clientId", out var clientId);
        fields.TryGetValue("clientSecret", out var clientSecret);
        fields.TryGetValue("scope", out var scope);
        fields.TryGetValue("grant", out var grant);
        fields.TryGetValue("tokenPath", out var tokenPath);
        fields.TryGetValue("provider", out var provider);

        var request = new OAuth2TokenRequest
        {
            TokenUrl = tokenUrl,
            ClientId = clientId ?? string.Empty,
            ClientSecret = clientSecret ?? string.Empty,
            Scope = scope,
            GrantType = !string.IsNullOrWhiteSpace(grant) ? grant : "client_credentials",
            TokenPath = tokenPath
        };

        // 按 provider 套用内置取 token 策略（如钉钉 GET+query+errcode 判定）；
        // 不暴露 5 个陌生策略字段给用户，由引擎内置填充。
        OAuth2ProviderTemplates.Apply(request, provider);

        var cacheKey = OAuth2TokenService.ComputeCacheKey(
            credential.Name, tokenUrl, request.ClientId, scope, request.GrantType);

        var token = await _tokenService.GetOrRefreshTokenAsync(cacheKey, request, cancellationToken)
            .ConfigureAwait(false);

        fields["accessToken"] = token.AccessToken;
        fields["tokenType"] = token.TokenType ?? "Bearer";
        if (token.ExpiresIn.HasValue)
        {
            fields["expiresIn"] = token.ExpiresIn.Value.ToString();
            fields["expiresAt"] = DateTime.UtcNow.AddSeconds(token.ExpiresIn.Value).ToString("O");
        }
        else
        {
            fields["expiresIn"] = string.Empty;
            fields["expiresAt"] = string.Empty;
        }

        if (!string.IsNullOrEmpty(token.RefreshToken))
        {
            fields["refreshToken"] = token.RefreshToken;
        }

        if (!string.IsNullOrEmpty(token.Scope))
        {
            fields["issuedScope"] = token.Scope;
        }

        return credential;
    }
}
