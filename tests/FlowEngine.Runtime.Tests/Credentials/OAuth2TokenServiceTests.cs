using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FlowEngine.Core.Exceptions;
using FlowEngine.Runtime.Credentials;

namespace FlowEngine.Runtime.Tests.Credentials;

public sealed class OAuth2TokenServiceTests
{
    [Fact]
    public async Task GetOrRefreshTokenAsync_FirstCall_FetchesToken_And_SecondCall_UsesCache()
    {
        var handler = new FakeTokenHandler();
        var factory = new StubHttpClientFactory(handler);
        var service = new OAuth2TokenService(factory);

        var request = CreateRequest();
        var cacheKey = OAuth2TokenService.ComputeCacheKey("cred", request.TokenUrl, request.ClientId, request.Scope, request.GrantType);

        var first = await service.GetOrRefreshTokenAsync(cacheKey, request);
        var second = await service.GetOrRefreshTokenAsync(cacheKey, request);

        Assert.Equal("tok-1", first.AccessToken);
        Assert.Equal("tok-1", second.AccessToken);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_DifferentClientIds_DoNotShareCache()
    {
        var handler = new FakeTokenHandler();
        var factory = new StubHttpClientFactory(handler);
        var service = new OAuth2TokenService(factory);

        var requestA = CreateRequest();
        var requestB = new OAuth2TokenRequest
        {
            TokenUrl = requestA.TokenUrl,
            ClientId = "other-client-id",
            ClientSecret = requestA.ClientSecret,
            Scope = requestA.Scope,
            GrantType = requestA.GrantType,
        };
        var cacheKeyA = OAuth2TokenService.ComputeCacheKey("cred", requestA.TokenUrl, requestA.ClientId, requestA.Scope, requestA.GrantType);
        var cacheKeyB = OAuth2TokenService.ComputeCacheKey("cred", requestB.TokenUrl, requestB.ClientId, requestB.Scope, requestB.GrantType);

        var first = await service.GetOrRefreshTokenAsync(cacheKeyA, requestA);
        var second = await service.GetOrRefreshTokenAsync(cacheKeyB, requestB);

        Assert.Equal("tok-1", first.AccessToken);
        Assert.Equal("tok-2", second.AccessToken);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetOrRefreshTokenAsync_NearExpiry_TriggersRefresh()
    {
        var handler = new FakeTokenHandler();
        var factory = new StubHttpClientFactory(handler);
        var service = new OAuth2TokenService(factory)
        {
            RefreshBufferSeconds = 5
        };

        var request = CreateRequest();
        var cacheKey = OAuth2TokenService.ComputeCacheKey("cred", request.TokenUrl, request.ClientId, request.Scope, request.GrantType);

        var first = await service.GetOrRefreshTokenAsync(cacheKey, request);
        Assert.Equal("tok-1", first.AccessToken);
        Assert.Equal(1, handler.CallCount);

        // 将缓存项设为即将过期（剩余 1 秒，低于 5 秒缓冲）
        SetCacheExpiresAt(service, cacheKey, DateTimeOffset.UtcNow.AddSeconds(1));

        var second = await service.GetOrRefreshTokenAsync(cacheKey, request);
        Assert.Equal("tok-2", second.AccessToken);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetTokenAsync_5xx_RetriesWithBackoff_AndEventuallySucceeds()
    {
        var handler = new FakeTokenHandler();
        handler.FailuresBeforeSuccess = 3;
        var factory = new StubHttpClientFactory(handler);
        var service = new OAuth2TokenService(factory)
        {
            MaxRetries = 3
        };

        var request = CreateRequest();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await service.GetTokenAsync(request);
        stopwatch.Stop();

        Assert.Equal("tok-after-retry", response.AccessToken);
        Assert.Equal(4, handler.CallCount);
        // 退避 1s + 2s + 4s
        Assert.True(stopwatch.ElapsedMilliseconds >= 6500, "应存在指数退避延迟");
    }

    [Fact]
    public async Task GetTokenAsync_TokenPath_ExtractsNestedAccessToken()
    {
        var handler = new FakeTokenHandler
        {
            ResponseShape = ResponseShape.Nested
        };
        var factory = new StubHttpClientFactory(handler);
        var service = new OAuth2TokenService(factory);

        var request = CreateRequest();
        request.TokenPath = "result.access_token";

        var response = await service.GetTokenAsync(request);

        Assert.Equal("nested-tok", response.AccessToken);
    }

    [Fact]
    public async Task GetTokenAsync_4xx_ReturnsBusinessException_WithoutRetry()
    {
        var handler = new FakeTokenHandler
        {
            ResponseShape = ResponseShape.Unauthorized
        };
        var factory = new StubHttpClientFactory(handler);
        var service = new OAuth2TokenService(factory);

        var request = CreateRequest();

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.GetTokenAsync(request));
        Assert.Contains("401", ex.Message);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetTokenAsync_DingtalkProvider_GetQueryAppkeyAppsecret_ErrcodeZero_Success()
    {
        // 阶段零 0.1：钉钉 provider 内置策略 —— GET + query appkey/appsecret + errcode==0 判定
        var handler = new DingtalkTokenHandler();
        var factory = new StubHttpClientFactory(handler);
        var service = new OAuth2TokenService(factory);

        var request = new OAuth2TokenRequest
        {
            TokenUrl = "https://oapi.dingtalk.com/gettoken",
            ClientId = "my-appkey",
            ClientSecret = "my-appsecret"
        };
        OAuth2ProviderTemplates.Apply(request, "dingtalk");

        var response = await service.GetTokenAsync(request);

        Assert.Equal("DT-TOKEN", response.AccessToken);
        Assert.Equal("GET", handler.LastMethod);
        Assert.Contains("appkey=my-appkey", handler.LastQuery);
        Assert.Contains("appsecret=my-appsecret", handler.LastQuery);
    }

    [Fact]
    public async Task GetTokenAsync_DingtalkProvider_ErrcodeNonZero_ThrowsBusinessException()
    {
        // 钉钉 errcode != 0 但 HTTP 200 时，应判定为业务失败
        var handler = new DingtalkTokenHandler(errcode: 88);
        var factory = new StubHttpClientFactory(handler);
        var service = new OAuth2TokenService(factory);

        var request = new OAuth2TokenRequest
        {
            TokenUrl = "https://oapi.dingtalk.com/gettoken",
            ClientId = "my-appkey",
            ClientSecret = "my-appsecret"
        };
        OAuth2ProviderTemplates.Apply(request, "dingtalk");

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.GetTokenAsync(request));
        Assert.Contains("errcode", ex.Message);
    }

    [Fact]
    public async Task GetTokenAsync_StandardProvider_PostForm_Unchanged()
    {
        // 标准 client_credentials 行为不受影响（POST + form client_id/client_secret）
        var handler = new FakeTokenHandler();
        var factory = new StubHttpClientFactory(handler);
        var service = new OAuth2TokenService(factory);

        var request = CreateRequest();
        OAuth2ProviderTemplates.Apply(request, "standard");

        var response = await service.GetTokenAsync(request);

        Assert.Equal("tok-1", response.AccessToken);
        Assert.Equal("POST", handler.LastMethod);
    }

    private static OAuth2TokenRequest CreateRequest()
    {
        return new OAuth2TokenRequest
        {
            TokenUrl = "http://example.com/oauth2/token",
            ClientId = "client-id",
            ClientSecret = "client-secret",
            Scope = "read",
            GrantType = "client_credentials"
        };
    }

    private static void SetCacheExpiresAt(OAuth2TokenService service, string cacheKey, DateTimeOffset expiresAt)
    {
        var field = typeof(OAuth2TokenService).GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cache = (System.Collections.IDictionary?)field?.GetValue(service);
        var entry = cache?[cacheKey];
        if (entry is null)
        {
            return;
        }

        var expiresAtProperty = entry.GetType().GetProperty("ExpiresAt");
        expiresAtProperty?.SetValue(entry, expiresAt);
    }

    private enum ResponseShape
    {
        Default,
        Nested,
        Unauthorized
    }

    private sealed class FakeTokenHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public int FailuresBeforeSuccess { get; set; }
        public ResponseShape ResponseShape { get; set; } = ResponseShape.Default;
        public string? LastMethod { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastMethod = request.Method.Method;

            if (ResponseShape == ResponseShape.Unauthorized)
            {
                var error = new JsonObject { ["error"] = "invalid_client" };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent(error.ToJsonString(), Encoding.UTF8, "application/json")
                });
            }

            if (CallCount <= FailuresBeforeSuccess)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("server error", Encoding.UTF8, "text/plain")
                });
            }

            JsonNode body;
            if (ResponseShape == ResponseShape.Nested)
            {
                body = new JsonObject
                {
                    ["result"] = new JsonObject
                    {
                        ["access_token"] = "nested-tok",
                        ["token_type"] = "Bearer",
                        ["expires_in"] = 3600
                    }
                };
            }
            else
            {
                var token = CallCount == 1 && FailuresBeforeSuccess == 0
                    ? "tok-1"
                    : CallCount == 2 && FailuresBeforeSuccess == 0
                        ? "tok-2"
                        : "tok-after-retry";

                body = new JsonObject
                {
                    ["access_token"] = token,
                    ["token_type"] = "Bearer",
                    ["expires_in"] = 3600
                };
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    private sealed class DingtalkTokenHandler : HttpMessageHandler
    {
        private readonly int _errcode;
        public string? LastMethod { get; private set; }
        public string? LastQuery { get; private set; }

        public DingtalkTokenHandler(int errcode = 0) => _errcode = errcode;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method.Method;
            LastQuery = request.RequestUri?.Query ?? string.Empty;

            var body = new JsonObject
            {
                ["errcode"] = _errcode,
                ["errmsg"] = _errcode == 0 ? "ok" : "invalid",
                ["access_token"] = "DT-TOKEN"
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            });
        }
    }
}
