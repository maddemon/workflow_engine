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

public sealed class HttpToolNodeTests
{
    [Fact]
    public async Task Execute_MissingUrl_ReturnsError()
    {
        var node = new HttpToolNode { Url = "" };
        var context = CreateContext(new JsonObject { ["path"] = "test" });

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("MissingUrl", result.Error?.Code);
    }

    [Fact]
    public async Task Execute_WithResolvedUrl_SendsRequestAndReturnsSuccess()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var pool = new StubHttpClientPool(client);

        var node = new HttpToolNode
        {
            Url = ResolvedUrl("http://example.com/api"),
            Method = HttpMethodOption.Get
        };
        var context = CreateContext(new JsonObject(), pool);

        var result = await node.ExecuteAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("http://example.com/api", handler.LastRequest?.RequestUri?.ToString());
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

    // 仅用于满足 NodeExecutionContext 的必填属性；本测试不验证凭证/日志分支。
    private sealed class NullCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken ct = default) =>
            Task.FromResult(new CredentialValue { Name = "null", Type = "apiKey" });

        public Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken ct = default) =>
            Task.FromResult<CredentialValue?>(null);
    }

    // 仅用于满足 NodeExecutionContext 的必填属性；本测试不验证日志分支。
    private sealed class NullExecutionLogger : IExecutionLogger
    {
        public void LogInformation(string message, params object?[] args) { }
        public void LogWarning(string message, params object?[] args) { }
        public void LogError(Exception? exception, string message, params object?[] args) { }
    }

    [Fact]
    public void ToolNode_HasCorrectMetadata()
    {
        var node = new HttpToolNode();
        Assert.Equal("httpTool", node.TypeName);
        Assert.Equal("HTTP Tool", node.DisplayName);
        Assert.Equal("AI", node.Category);
        Assert.False(node.DefaultIsEntry);
    }

    [Fact]
    public void ToolNode_HasInputAndOutputPorts()
    {
        var node = new HttpToolNode();
        Assert.Equal(3, node.Ports.Count);
        Assert.Contains(node.Ports, p => p.Name == FlowConstants.PortNames.Input && p.Direction == PortDirection.Input);
        Assert.Contains(node.Ports, p => p.Name == FlowConstants.PortNames.Output && p.Direction == PortDirection.Output);
    }

    private static NodeExecutionContext CreateContext(JsonObject inputPayload, IHttpClientPool? pool = null)
    {
        return new NodeExecutionContext
        {
            Node = new NodeDefinition
            {
                Id = "Test Http",
                TypeName = "httpTool",
                Name = "Test Http",
                Parameters = [],
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
                            Data = inputPayload,
                            Success = true,
                            SourceIndex = 0
                        }
                    ]
                }
            },
            RawParameters = new Dictionary<string, object>(),
            ResolvedParameters = new Dictionary<string, object>(),
            Credentials = new NullCredentialAccessor(),
            Logger = new NullExecutionLogger(),
            HttpClientPool = pool,
            ScriptCache = new ScriptCache(Options.Create(new JsEngineOptions())),
            EngineOptions = new JsEngineOptions(),
            CancellationToken = CancellationToken.None
        };
    }
}
