using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FlowEngine.Core.Exceptions;
using FlowEngine.Runtime.Credentials;

namespace FlowEngine.Runtime.Tests.Credentials;

/// <summary>
/// OAuth2 令牌服务额外分支测试：校验、SSRF 防护、非 JSON 响应、业务错误码判定、JSON 对象令牌提取、缓存清理。
/// </summary>
public sealed class OAuth2TokenServiceExtraTests
{
    [Fact]
    public async Task GetTokenAsync_EmptyTokenUrl_ThrowsBusinessException()
    {
        var service = new OAuth2TokenService(new StubHttpClientFactory(new JsonTokenHandler()));

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.GetTokenAsync(new OAuth2TokenRequest
        {
            TokenUrl = "   ",
            ClientId = "id",
            ClientSecret = "secret"
        }));

        Assert.Contains("tokenUrl", ex.Message);
    }

    [Fact]
    public async Task GetTokenAsync_EmptyClientId_ThrowsBusinessException()
    {
        var service = new OAuth2TokenService(new StubHttpClientFactory(new JsonTokenHandler()));

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.GetTokenAsync(new OAuth2TokenRequest
        {
            TokenUrl = "https://example.com/token",
            ClientId = string.Empty,
            ClientSecret = "secret"
        }));

        Assert.Contains("clientId", ex.Message);
    }

    [Fact]
    public async Task GetTokenAsync_EmptyClientSecret_ThrowsBusinessException()
    {
        var service = new OAuth2TokenService(new StubHttpClientFactory(new JsonTokenHandler()));

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.GetTokenAsync(new OAuth2TokenRequest
        {
            TokenUrl = "https://example.com/token",
            ClientId = "id",
            ClientSecret = "   "
        }));

        Assert.Contains("clientSecret", ex.Message);
    }

    [Fact]
    public async Task GetTokenAsync_InternalSsrfUrl_ThrowsBusinessException_WithoutSendingRequest()
    {
        // 127.0.0.1 为内网地址，应被 SSRF 防护拦截，且不实际发出请求。
        var handler = new JsonTokenHandler();
        var service = new OAuth2TokenService(new StubHttpClientFactory(handler));

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.GetTokenAsync(new OAuth2TokenRequest
        {
            TokenUrl = "http://127.0.0.1/internal/token",
            ClientId = "id",
            ClientSecret = "secret"
        }));

        Assert.Contains("SSRF", ex.Message);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GetTokenAsync_NonJsonResponse_ThrowsBusinessException()
    {
        var handler = new FixedTextHandler("not-json");
        var service = new OAuth2TokenService(new StubHttpClientFactory(handler));

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.GetTokenAsync(new OAuth2TokenRequest
        {
            TokenUrl = "https://example.com/token",
            ClientId = "id",
            ClientSecret = "secret"
        }));

        Assert.Contains("非 JSON", ex.Message);
    }

    [Fact]
    public async Task GetTokenAsync_BusinessErrorPath_MatchingSuccessValue_Succeeds()
    {
        // 配置错误码路径 code，且返回值命中成功值集合：应视为成功而非抛异常。
        var body = new JsonObject
        {
            ["code"] = "0",
            ["access_token"] = "ok-tok"
        };
        var handler = new JsonTokenHandler(body);
        var service = new OAuth2TokenService(new StubHttpClientFactory(handler));

        var request = new OAuth2TokenRequest
        {
            TokenUrl = "https://example.com/token",
            ClientId = "id",
            ClientSecret = "secret",
            ResponseErrorPath = "code",
            ResponseSuccessValues = new List<string> { "0" }
        };

        var response = await service.GetTokenAsync(request);

        Assert.Equal("ok-tok", response.AccessToken);
    }

    [Fact]
    public async Task GetTokenAsync_TokenPathPointsToJsonObject_ExtractsRawObjectString()
    {
        // 令牌路径指向嵌套对象：应退化为原始 JSON 文本（去外层引号），而非抛 InvalidOperationException。
        var body = new JsonObject
        {
            ["access_token"] = new JsonObject { ["kid"] = "abc" }
        };
        var handler = new JsonTokenHandler(body);
        var service = new OAuth2TokenService(new StubHttpClientFactory(handler));

        var response = await service.GetTokenAsync(new OAuth2TokenRequest
        {
            TokenUrl = "https://example.com/token",
            ClientId = "id",
            ClientSecret = "secret"
        });

        Assert.Contains("kid", response.AccessToken);
    }

    [Fact]
    public async Task Dispose_ClearsCache_ForcesRefetchOnNextCall()
    {
        var handler = new JsonTokenHandler();
        var service = new OAuth2TokenService(new StubHttpClientFactory(handler));

        var request = new OAuth2TokenRequest
        {
            TokenUrl = "https://example.com/token",
            ClientId = "id",
            ClientSecret = "secret"
        };
        var cacheKey = OAuth2TokenService.ComputeCacheKey("cred", request.TokenUrl, request.ClientId, request.Scope, request.GrantType);

        await service.GetOrRefreshTokenAsync(cacheKey, request);
        service.Dispose();
        await service.GetOrRefreshTokenAsync(cacheKey, request);

        // Dispose 清空缓存后，第二次调用应重新请求。
        Assert.Equal(2, handler.CallCount);
    }

    private static OAuth2TokenRequest CreateRequest() => new()
    {
        TokenUrl = "https://example.com/oauth2/token",
        ClientId = "client-id",
        ClientSecret = "client-secret",
        Scope = "read",
        GrantType = "client_credentials"
    };

    private sealed class JsonTokenHandler : HttpMessageHandler
    {
        private readonly JsonNode _body;
        public int CallCount { get; private set; }

        public JsonTokenHandler(JsonNode? body = null)
        {
            _body = body ?? new JsonObject
            {
                ["access_token"] = "tok-1",
                ["token_type"] = "Bearer",
                ["expires_in"] = 3600
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body.ToJsonString(), Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FixedTextHandler : HttpMessageHandler
    {
        private readonly string _text;
        public FixedTextHandler(string text) => _text = text;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_text, Encoding.UTF8, "text/plain")
            });
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
