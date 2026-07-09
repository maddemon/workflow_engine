using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using FlowEngine.Plugins.Standard;
using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Executor;
using FlowEngine.Runtime.Registry;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

public sealed class PaginateNodeTests
{
    [Fact]
    public async Task ExecuteAsync_IteratesWithCursor_AndAggregatesItems()
    {
        // 用桩 HttpMessageHandler 模拟游标分页接口：cursor=0/1/2 各返回 2 条，
        // cursor>=2 时 next_cursor 置空，触发 terminateWhen: $nextCursor == ''
        var handler = new StubPaginateHandler();
        using var client = new HttpClient(handler);
        var pool = new StubHttpClientPool(client);
        var credentialAccessor = new NullCredentialAccessor();
        var registry = new NodeRegistry(new List<INodeType> { new PaginateNode() }, NullLogger<NodeRegistry>.Instance);
        var factory = new NodeExecutionContextFactory(
            registry,
            new ParameterResolver(NullLogger<ParameterResolver>.Instance),
            credentialAccessor,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var config = new Dictionary<string, object>
        {
            ["url"] = "\"http://example.com/api?cursor=\" + $cursor",
            ["method"] = "GET",
            ["cursorInitial"] = "0",
            ["cursorType"] = "number",
            ["nextCursorPath"] = "result.next_cursor",
            ["itemsPath"] = "result.list",
            ["terminateWhen"] = "$nextCursor == ''",
            ["maxPages"] = "10"
        };

        var context = new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "t" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = Guid.NewGuid(),
                TypeName = "paginate",
                Name = "pag",
                Parameters = config
            },
            Inputs = new Dictionary<string, DataBatch>(),
            RawParameters = config,
            ResolvedParameters = config,
            Credentials = credentialAccessor,
            CancellationToken = CancellationToken.None,
            HttpClientPool = pool,
            NodeRegistry = registry,
            ContextFactory = factory
        };

        var node = new PaginateNode();
        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message ?? "PaginateNode failed without error");
        // 3 页 × 2 条 = 6 条
        Assert.Equal(6, result.Output.Items.Count);
        // 桩共被调用 3 次（cursor=0,1,2）
        Assert.Equal(3, handler.CallCount);
        // 验证游标确实按 0→1→2 推进：id 集合应为 1,2,11,12,21,22
        var ids = new List<int>();
        foreach (var item in result.Output.Items)
        {
            if (item.Data is JsonObject obj && obj["id"] is JsonValue v && v.TryGetValue(out int id))
            {
                ids.Add(id);
            }
        }

        Assert.Equal(new[] { 1, 2, 11, 12, 21, 22 }, ids);
    }

    [Fact]
    public async Task ExecuteAsync_TerminateWhenStopsEarly_ReturnsPartialItems()
    {
        // terminateWhen 在第 2 页就触发（$page >= 2），仅返回前 2 页 4 条
        var handler = new StubPaginateHandler();
        using var client = new HttpClient(handler);
        var pool = new StubHttpClientPool(client);
        var credentialAccessor = new NullCredentialAccessor();
        var registry = new NodeRegistry(new List<INodeType> { new PaginateNode() }, NullLogger<NodeRegistry>.Instance);
        var factory = new NodeExecutionContextFactory(
            registry,
            new ParameterResolver(NullLogger<ParameterResolver>.Instance),
            credentialAccessor,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var config = new Dictionary<string, object>
        {
            ["url"] = "\"http://example.com/api?cursor=\" + $cursor",
            ["method"] = "GET",
            ["cursorInitial"] = "0",
            ["cursorType"] = "number",
            ["nextCursorPath"] = "result.next_cursor",
            ["itemsPath"] = "result.list",
            ["terminateWhen"] = "$page >= 2",
            ["maxPages"] = "10"
        };

        var context = new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "t" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = Guid.NewGuid(),
                TypeName = "paginate",
                Name = "pag",
                Parameters = config
            },
            Inputs = new Dictionary<string, DataBatch>(),
            RawParameters = config,
            ResolvedParameters = config,
            Credentials = credentialAccessor,
            CancellationToken = CancellationToken.None,
            HttpClientPool = pool,
            NodeRegistry = registry,
            ContextFactory = factory
        };

        var node = new PaginateNode();
        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message ?? "PaginateNode failed without error");
        // 第 0 页：items=[1,2] → terminateWhen($page=0) false → 继续
        // 第 1 页：items=[11,12] → terminateWhen($page=1) false → 继续
        // 第 2 页：items=[21,22] → terminateWhen($page=2) true → 停止，但第2页的items仍被收集
        Assert.Equal(6, result.Output.Items.Count);
        // 但只调用了 3 次（第 2 页后才终止）
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_HttpError_ReturnsErrorResult()
    {
        // 服务端返回 500 错误
        var handler = new StubErrorHandler(HttpStatusCode.InternalServerError);
        using var client = new HttpClient(handler);
        var pool = new StubHttpClientPool(client);
        var credentialAccessor = new NullCredentialAccessor();
        var registry = new NodeRegistry(new List<INodeType> { new PaginateNode() }, NullLogger<NodeRegistry>.Instance);
        var factory = new NodeExecutionContextFactory(
            registry,
            new ParameterResolver(NullLogger<ParameterResolver>.Instance),
            credentialAccessor,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var config = new Dictionary<string, object>
        {
            ["url"] = "\"http://example.com/api?cursor=\" + $cursor",
            ["method"] = "GET",
            ["cursorInitial"] = "0",
            ["cursorType"] = "number",
            ["nextCursorPath"] = "result.next_cursor",
            ["itemsPath"] = "result.list",
            ["terminateWhen"] = "$nextCursor == ''",
            ["maxPages"] = "10"
        };

        var context = new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "t" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = Guid.NewGuid(),
                TypeName = "paginate",
                Name = "pag",
                Parameters = config
            },
            Inputs = new Dictionary<string, DataBatch>(),
            RawParameters = config,
            ResolvedParameters = config,
            Credentials = credentialAccessor,
            CancellationToken = CancellationToken.None,
            HttpClientPool = pool,
            NodeRegistry = registry,
            ContextFactory = factory
        };

        var node = new PaginateNode();
        var result = await node.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    private sealed class NullCredentialAccessor : ICredentialAccessor
    {
        public Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
            => Task.FromResult<CredentialValue>(null!);
    }

    private sealed class StubHttpClientPool : IHttpClientPool
    {
        private readonly HttpClient _client;
        public StubHttpClientPool(HttpClient client) => _client = client;
        public HttpClient GetClient(string? name = null) => _client;
    }

    private sealed class StubPaginateHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var query = request.RequestUri?.Query ?? string.Empty;
            var cursor = 0;
            foreach (var part in query.TrimStart('?').Split('&'))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && kv[0] == "cursor" && int.TryParse(kv[1], out var c))
                {
                    cursor = c;
                }
            }

            var nextCursor = cursor < 2 ? (cursor + 1).ToString() : string.Empty;
            var body = new JsonObject
            {
                ["result"] = new JsonObject
                {
                    ["list"] = new JsonArray
                    {
                        JsonNode.Parse($"{{\"id\":{cursor * 10 + 1}}}")!,
                        JsonNode.Parse($"{{\"id\":{cursor * 10 + 2}}}")!
                    },
                    ["next_cursor"] = nextCursor
                }
            };

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class StubErrorHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        public StubErrorHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent("server error", Encoding.UTF8, "text/plain")
            });
        }
    }
}
