using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Runtime.Credentials;

namespace FlowEngine.Runtime.Tests.Credentials;

/// <summary>
/// OAuth2 凭据访问器装饰器测试：覆盖非 oauth2 透传、缺失 tokenUrl 透传、以及成功获取令牌后的字段注入语义。
/// </summary>
public sealed class OAuth2CredentialAccessorTests
{
    private sealed class StubInnerAccessor : ICredentialAccessor
    {
        private readonly CredentialValue _value;
        public StubInnerAccessor(CredentialValue value) => _value = value;
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult(_value);
        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue?>(_value);
    }

    private sealed class StubTokenService : IOAuth2TokenService
    {
        public int CallCount { get; private set; }
        public bool HasExpiresIn { get; set; } = true;
        public bool HasRefreshAndScope { get; set; } = true;

        public Task<OAuth2TokenResponse> GetTokenAsync(OAuth2TokenRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Build());

        public Task<OAuth2TokenResponse> GetOrRefreshTokenAsync(string cacheKey, OAuth2TokenRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Build());
        }

        private OAuth2TokenResponse Build() => new()
        {
            AccessToken = "AT-123",
            TokenType = "Bearer",
            ExpiresIn = HasExpiresIn ? 3600 : null,
            RefreshToken = HasRefreshAndScope ? "RT-456" : null,
            Scope = HasRefreshAndScope ? "read" : null
        };
    }

    [Fact]
    public async Task GetCredentialAsync_NonOAuth2Type_ReturnsUnchanged_AndDoesNotCallTokenService()
    {
        var inner = new StubInnerAccessor(new CredentialValue { Type = "apiKey", Name = "k" });
        var tokenService = new StubTokenService();
        var accessor = new OAuth2CredentialAccessor(inner, tokenService);

        var result = await accessor.GetCredentialAsync(Guid.NewGuid());

        Assert.Equal("apiKey", result.Type);
        Assert.False(result.Fields.ContainsKey("accessToken"));
        Assert.Equal(0, tokenService.CallCount);
    }

    [Fact]
    public async Task GetCredentialAsync_OAuth2MissingTokenUrl_ReturnsUnchanged()
    {
        var inner = new StubInnerAccessor(new CredentialValue
        {
            Type = "oauth2",
            Name = "cred",
            Fields = new Dictionary<string, string> { ["clientId"] = "id", ["clientSecret"] = "secret" }
        });
        var tokenService = new StubTokenService();
        var accessor = new OAuth2CredentialAccessor(inner, tokenService);

        var result = await accessor.GetCredentialAsync(Guid.NewGuid());

        Assert.False(result.Fields.ContainsKey("accessToken"));
        Assert.Equal(0, tokenService.CallCount);
    }

    [Fact]
    public async Task GetCredentialAsync_OAuth2WithTokenUrl_EnrichesFields()
    {
        var inner = new StubInnerAccessor(new CredentialValue
        {
            Type = "oauth2",
            Name = "cred",
            Fields = new Dictionary<string, string>
            {
                ["tokenUrl"] = "https://example.com/token",
                ["clientId"] = "id",
                ["clientSecret"] = "secret"
            }
        });
        var tokenService = new StubTokenService();
        var accessor = new OAuth2CredentialAccessor(inner, tokenService);

        var result = await accessor.GetCredentialAsync(Guid.NewGuid());

        Assert.Equal("AT-123", result.Fields["accessToken"]);
        Assert.Equal("Bearer", result.Fields["tokenType"]);
        Assert.Equal("3600", result.Fields["expiresIn"]);
        Assert.NotEmpty(result.Fields["expiresAt"]);
        Assert.Equal("RT-456", result.Fields["refreshToken"]);
        Assert.Equal("read", result.Fields["issuedScope"]);
        Assert.Equal(1, tokenService.CallCount);
    }

    [Fact]
    public async Task GetCredentialAsync_OAuth2WithoutExpiresIn_SetsEmptyExpiryFields()
    {
        var inner = new StubInnerAccessor(new CredentialValue
        {
            Type = "oauth2",
            Name = "cred",
            Fields = new Dictionary<string, string>
            {
                ["tokenUrl"] = "https://example.com/token",
                ["clientId"] = "id",
                ["clientSecret"] = "secret"
            }
        });
        var tokenService = new StubTokenService { HasExpiresIn = false, HasRefreshAndScope = false };
        var accessor = new OAuth2CredentialAccessor(inner, tokenService);

        var result = await accessor.GetCredentialAsync(Guid.NewGuid());

        Assert.Equal(string.Empty, result.Fields["expiresIn"]);
        Assert.Equal(string.Empty, result.Fields["expiresAt"]);
        // 无 refreshToken/scope 时不应写入对应字段
        Assert.False(result.Fields.ContainsKey("refreshToken"));
        Assert.False(result.Fields.ContainsKey("issuedScope"));
    }

    [Fact]
    public async Task GetCredentialByNameAsync_InnerReturnsNull_ReturnsNull()
    {
        var inner = new StubInnerAccessorNull();
        var tokenService = new StubTokenService();
        var accessor = new OAuth2CredentialAccessor(inner, tokenService);

        var result = await accessor.GetCredentialByNameAsync("missing");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCredentialByNameAsync_OAuth2_EnrichesFields()
    {
        var inner = new StubInnerAccessor(new CredentialValue
        {
            Type = "oauth2",
            Name = "cred",
            Fields = new Dictionary<string, string>
            {
                ["tokenUrl"] = "https://example.com/token",
                ["clientId"] = "id",
                ["clientSecret"] = "secret"
            }
        });
        var tokenService = new StubTokenService();
        var accessor = new OAuth2CredentialAccessor(inner, tokenService);

        var result = await accessor.GetCredentialByNameAsync("cred");

        Assert.NotNull(result);
        Assert.Equal("AT-123", result!.Fields["accessToken"]);
    }

    private sealed class StubInnerAccessorNull : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue>(new CredentialValue());
        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue?>(null);
    }
}
