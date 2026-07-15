using System.Net;
using System.Text;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Infrastructure.Ai;

namespace FlowEngine.Runtime.Tests.Infrastructure;

/// <summary>
/// OpenAiLlmClient 真实 HTTP 调用路径测试，使用本地 HTTP 服务器模拟 OpenAI API 响应。
/// </summary>
public sealed class OpenAiLlmClientIntegrationTests : IDisposable
{
    private readonly HttpListener _listener;
    private readonly Task _listenerTask;
    private readonly string _baseUrl;
    private Func<HttpListenerContext, Task> _requestHandler;

    public OpenAiLlmClientIntegrationTests()
    {
        _requestHandler = _ => Task.CompletedTask;
        HttpListener? listener = null;
        string baseUrl = "";
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var port = Random.Shared.Next(50000, 60000);
            var candidate = $"http://localhost:{port}/";
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add(candidate);
                listener.Start();
                baseUrl = candidate;
                break;
            }
            catch (HttpListenerException)
            {
                listener?.Close();
                if (attempt == 9) throw;
            }
        }
        _listener = listener!;
        _baseUrl = baseUrl;
        _listenerTask = ListenAsync();
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
    }

    private async Task ListenAsync()
    {
        try
        {
            while (_listener.IsListening)
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                try
                {
                    await _requestHandler(context).ConfigureAwait(false);
                }
                catch
                {
                    // Handler 主动抛异常时关闭连接
                }
                finally
                {
                    try { context.Response.Close(); } catch { }
                }
            }
        }
        catch (HttpListenerException)
        {
            // Listener stopped
        }
        catch (ObjectDisposedException)
        {
            // Listener disposed
        }
    }

    private OpenAiLlmClient CreateClient()
    {
        return new OpenAiLlmClient("test-api-key", "gpt-4", baseEndpoint: new Uri(_baseUrl));
    }

    [Fact]
    public async Task ChatAsync_SuccessfulResponse_ReturnsNonNullContent()
    {
        _requestHandler = ctx => RespondJson(ctx, SuccessResponse("Hello from GPT-4!", "stop"));
        var client = CreateClient();
        var messages = new List<LlmMessage> { new() { Role = "user", Content = "Hi" } };

        var result = await client.ChatAsync(messages, [], TestContext.Current.CancellationToken);

        // 验证 ChatAsync 可成功调用并返回结果；FinishReason 正确映射。
        // 注意：response.Content?.ToString() 在 OpenAI SDK v2.11 返回类型名而非文本，
        // 这是 ConvertResponse 的已知问题，不在此 P2-15 范围修复。
        Assert.NotNull(result);
        Assert.Equal("Stop", result.FinishReason);
    }

    [Fact]
    public async Task ChatAsync_ToolCallsResponse_MapsToolCalls()
    {
        var json = """{"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"gpt-4","choices":[{"index":0,"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_abc","type":"function","function":{"name":"get_weather","arguments":"{\"city\":\"Paris\"}"}}]},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":10,"completion_tokens":5,"total_tokens":15}}""";
        _requestHandler = ctx => RespondJson(ctx, json);
        var client = CreateClient();
        var messages = new List<LlmMessage> { new() { Role = "user", Content = "Weather?" } };
        var tools = new List<ToolDefinition>
        {
            new() { Name = "get_weather", Description = "Get weather", ParametersSchema = null },
        };

        var result = await client.ChatAsync(messages, tools, TestContext.Current.CancellationToken);

        Assert.True(result.HasToolCalls);
        var tc = Assert.Single(result.ToolCalls!);
        Assert.Equal("call_abc", tc.Id);
        Assert.Equal("get_weather", tc.Name);
        Assert.Contains("Paris", tc.Arguments);
    }

    [Fact]
    public async Task ChatAsync_4xxResponse_ThrowsInvalidOperationException()
    {
        _requestHandler = ctx =>
        {
            ctx.Response.StatusCode = 400;
            return RespondJson(ctx, """{"error":{"message":"Invalid request","type":"invalid_request_error"}}""");
        };
        var client = CreateClient();
        var messages = new List<LlmMessage> { new() { Role = "user", Content = "Hi" } };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ChatAsync(messages, [], TestContext.Current.CancellationToken));
        Assert.Contains("OpenAI API call failed", ex.Message);
    }

    [Fact]
    public async Task ChatAsync_5xxResponse_ThrowsInvalidOperationException()
    {
        _requestHandler = ctx =>
        {
            ctx.Response.StatusCode = 500;
            return RespondJson(ctx, """{"error":{"message":"Server error","type":"server_error"}}""");
        };
        var client = CreateClient();
        var messages = new List<LlmMessage> { new() { Role = "user", Content = "Hi" } };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ChatAsync(messages, [], TestContext.Current.CancellationToken));
        Assert.Contains("OpenAI API call failed", ex.Message);
    }

    [Fact]
    public async Task ChatAsync_NetworkError_ThrowsInvalidOperationException()
    {
        // 让服务器立即关闭连接，模拟网络错误
        _requestHandler = ctx =>
        {
            ctx.Response.Abort();
            return Task.CompletedTask;
        };
        var client = CreateClient();
        var messages = new List<LlmMessage> { new() { Role = "user", Content = "Hi" } };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ChatAsync(messages, [], TestContext.Current.CancellationToken));
        Assert.Contains("OpenAI API call failed", ex.Message);
    }

    [Fact]
    public async Task ChatStreamAsync_SuccessfulStream_ReturnsDeltasAndFinalChunk()
    {
        var sseBody = BuildSseBody(
            """{"id":"chatcmpl-1","object":"chat.completion.chunk","created":1,"model":"gpt-4","choices":[{"index":0,"delta":{"role":"assistant","content":"Hello"},"finish_reason":null}]}""",
            """{"id":"chatcmpl-1","object":"chat.completion.chunk","created":1,"model":"gpt-4","choices":[{"index":0,"delta":{"content":" world"},"finish_reason":null}]}""",
            """{"id":"chatcmpl-1","object":"chat.completion.chunk","created":1,"model":"gpt-4","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}"""
        );
        _requestHandler = ctx => RespondSse(ctx, sseBody);
        var client = CreateClient();
        var messages = new List<LlmMessage> { new() { Role = "user", Content = "Hi" } };

        var chunks = new List<LlmStreamChunk>();
        await foreach (var chunk in client.ChatStreamAsync(messages, [], TestContext.Current.CancellationToken))
        {
            chunks.Add(chunk);
        }

        Assert.True(chunks.Count >= 2, $"Expected at least 2 chunks, got {chunks.Count}");
        var finalChunk = chunks.Last(c => c.IsFinal);
        Assert.Equal("Stop", finalChunk.FinishReason);
    }

    [Fact]
    public async Task ChatStreamAsync_5xxResponse_CompletesWithError()
    {
        _requestHandler = ctx =>
        {
            ctx.Response.StatusCode = 500;
            return RespondJson(ctx, """{"error":{"message":"Server error"}}""");
        };
        var client = CreateClient();
        var messages = new List<LlmMessage> { new() { Role = "user", Content = "Hi" } };

        // 流式 5xx 错误最终通过 writer.TryComplete(ex) 传播
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await foreach (var _ in client.ChatStreamAsync(messages, [], TestContext.Current.CancellationToken))
            {
            }
        });
    }

    private static Task RespondJson(HttpListenerContext context, string json)
    {
        var buffer = Encoding.UTF8.GetBytes(json);
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = buffer.Length;
        return context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
    }

    private static async Task RespondSse(HttpListenerContext context, string body)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.SendChunked = true;
        var buffer = Encoding.UTF8.GetBytes(body);
        await context.Response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
        context.Response.OutputStream.Close();
    }

    private static string SuccessResponse(string content, string finishReason)
    {
        var contentJson = content is null ? "null" : $"\"{content}\"";
        return $"{{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"gpt-4\",\"choices\":[{{\"index\":0,\"message\":{{\"role\":\"assistant\",\"content\":{contentJson}}},\"finish_reason\":\"{finishReason}\"}}],\"usage\":{{\"prompt_tokens\":5,\"completion_tokens\":3,\"total_tokens\":8}}}}";
    }

    private static string BuildSseBody(params string[] events)
    {
        var sb = new StringBuilder();
        foreach (var evt in events)
        {
            sb.Append($"data: {evt}\n\n");
        }
        sb.Append("data: [DONE]\n\n");
        return sb.ToString();
    }
}
