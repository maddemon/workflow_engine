using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Plugins.Standard;
using Microsoft.Extensions.Options;

namespace FlowEngine.Runtime.Tests.Plugins;

public sealed class HttpRequestNodeTests
{
    [Fact]
    public async Task ExecuteAsync_BearerWithOAuth2Credential_SendsAccessTokenHeader()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var pool = new StubHttpClientPool(client);
        var credentialId = Guid.NewGuid();
        var accessor = new OAuth2CredentialAccessorStub(credentialId, "oauth2-access-token");

        var node = new HttpRequestNode
        {
            Url = ResolvedUrl("http://example.com/api"),
            Method = HttpMethodOption.Get,
            Authentication = HttpRequestAuthMode.BearerToken,
            CredentialId = credentialId.ToString()
        };

        var context = CreateContext(accessor, pool);

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(1, handler.CallCount);
        var authHeader = handler.LastRequest?.Headers.Authorization?.ToString();
        Assert.Equal("Bearer oauth2-access-token", authHeader);
    }

    [Fact]
    public async Task ExecuteAsync_BearerWithApiKeyCredential_SendsApiKeyHeader()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var pool = new StubHttpClientPool(client);
        var credentialId = Guid.NewGuid();
        var accessor = new ApiKeyCredentialAccessorStub(credentialId, "sk-123");

        var node = new HttpRequestNode
        {
            Url = ResolvedUrl("http://example.com/api"),
            Method = HttpMethodOption.Get,
            Authentication = HttpRequestAuthMode.BearerToken,
            CredentialId = credentialId.ToString()
        };

        var context = CreateContext(accessor, pool);

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var authHeader = handler.LastRequest?.Headers.Authorization?.ToString();
        Assert.Equal("Bearer sk-123", authHeader);
    }

    [Fact]
    public async Task ExecuteAsync_MissingUrl_ReturnsError()
    {
        var node = new HttpRequestNode { Url = "" };
        var context = CreateContext(new NullCredentialAccessor(), new StubHttpClientPool(new HttpClient(new RecordingHandler())));

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("MissingUrl", result.Error?.Code);
    }

    private static NodeExecutionContext CreateContext(ICredentialAccessor accessor, IHttpClientPool pool)
    {
        return new NodeExecutionContext
        {
            Node = new NodeDefinition
            {
                Id = "HTTP",
                TypeName = "httpRequest",
                Name = "HTTP",
                Parameters = new Dictionary<string, object>(),
                Ports = [],
                ErrorStrategy = ErrorStrategy.Terminate
            },
            ExecutionId = Guid.NewGuid(),
            Inputs = new Dictionary<string, DataBatch>
            {
                [FlowConstants.PortNames.Input] = new()
                {
                    Items =
                    [
                        new DataItem
                        {
                            Data = new JsonObject(),
                            Success = true,
                            SourceIndex = 0
                        }
                    ]
                }
            },
            RawParameters = new Dictionary<string, object>(),
            ResolvedParameters = new Dictionary<string, object>(),
            Credentials = accessor,
            HttpClientPool = pool,
            ScriptCache = new ScriptCache(Options.Create(new JsEngineOptions())),
            EngineOptions = new JsEngineOptions(),
            CancellationToken = CancellationToken.None
        };
    }

    [Fact]
    public async Task ExecuteAsync_SuccessWhenTrue_ResponseOk_ReturnsSuccess()
    {
        // 阶段零 0.2：HTTP 2xx 且 successWhen 为真 → 节点成功
        var handler = new BodyRecordingHandler("{\"errcode\":0}");
        using var client = new HttpClient(handler);
        var pool = new StubHttpClientPool(client);

        var node = new HttpRequestNode
        {
            Url = ResolvedUrl("http://example.com/api"),
            Method = HttpMethodOption.Get,
            SuccessWhen = new Script
            {
                Source = "$json.errcode == 0",
                Language = ScriptLanguage.JavaScript,
                ReturnType = ScriptReturnType.Bool
            }
        };

        var context = CreateContext(new NullCredentialAccessor(), pool);
        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessWhenFalse_ResponseOkButBusinessFail_ReturnsError()
    {
        // 阶段零 0.2：HTTP 200 但 successWhen 为假（钉钉 errcode != 0）→ 节点失败，不自动重试
        var handler = new BodyRecordingHandler("{\"errcode\":500}");
        using var client = new HttpClient(handler);
        var pool = new StubHttpClientPool(client);

        var node = new HttpRequestNode
        {
            Url = ResolvedUrl("http://example.com/api"),
            Method = HttpMethodOption.Get,
            SuccessWhen = new Script
            {
                Source = "$json.errcode == 0",
                Language = ScriptLanguage.JavaScript,
                ReturnType = ScriptReturnType.Bool
            }
        };

        var context = CreateContext(new NullCredentialAccessor(), pool);
        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SuccessWhenFailed", result.Error?.Code);
    }

    private sealed class StubHttpClientPool : IHttpClientPool
    {
        private readonly HttpClient _client;
        public StubHttpClientPool(HttpClient client) => _client = client;
        public HttpClient GetClient(string? name = null) => _client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class OAuth2CredentialAccessorStub : ICredentialAccessor
    {
        private readonly Guid _id;
        private readonly string _accessToken;

        public OAuth2CredentialAccessorStub(Guid id, string accessToken)
        {
            _id = id;
            _accessToken = accessToken;
        }

        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken ct = default)
        {
            if (credentialId != _id)
            {
                return Task.FromResult(new CredentialValue { Name = "unknown", Type = "apiKey" });
            }

            return Task.FromResult(new CredentialValue
            {
                Name = "oauth2-cred",
                Type = "oauth2",
                Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["accessToken"] = _accessToken
                }
            });
        }

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken ct = default) =>
            Task.FromResult<CredentialValue?>(null);
    }

    private sealed class ApiKeyCredentialAccessorStub : ICredentialAccessor
    {
        private readonly Guid _id;
        private readonly string _apiKey;

        public ApiKeyCredentialAccessorStub(Guid id, string apiKey)
        {
            _id = id;
            _apiKey = apiKey;
        }

        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken ct = default)
        {
            if (credentialId != _id)
            {
                return Task.FromResult(new CredentialValue { Name = "unknown", Type = "apiKey" });
            }

            return Task.FromResult(new CredentialValue
            {
                Name = "api-key-cred",
                Type = "apiKey",
                Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["apiKey"] = _apiKey
                }
            });
        }

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken ct = default) =>
            Task.FromResult<CredentialValue?>(null);
    }

    private static Script ResolvedUrl(string url)
    {
        return new Script
        {
            Source = $"'{url}'",
            Language = ScriptLanguage.JavaScript,
            ReturnType = ScriptReturnType.String
        }.WithResolvedValue(JsonValue.Create(url));
    }

    private sealed class NullCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken ct = default) =>
            Task.FromResult(new CredentialValue { Name = "null", Type = "apiKey" });

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken ct = default) =>
            Task.FromResult<CredentialValue?>(null);
    }

    private sealed class BodyRecordingHandler : HttpMessageHandler
    {
        private readonly string _body;
        public int CallCount { get; private set; }

        public BodyRecordingHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }
}
