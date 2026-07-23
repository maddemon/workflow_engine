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

/// <summary>
/// readFile 节点测试：覆盖本地二进制/文本读取、文件不存在、空来源、URL 拉取、SSRF 拦截、
/// 自定义 BinaryField、非法编码、HTTP 客户端缺失、表达式来源等路径。
/// </summary>
public sealed class ReadFileNodeTests
{
    [Fact]
    public async Task ExecuteAsync_LocalBinaryFile_ReturnsBase64RoundTrip()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0xFE, 0xFF, 0xAB, 0xCD };
        var path = WriteTempFile(bytes, ".bin");

        try
        {
            var node = new ReadFileNode { Source = SourceOf(path) };

            var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

            Assert.True(result.Success, result.Error?.Message);
            Assert.Single(result.Output.Items);
            var data = GetString(result.Output.Items[0].Data, "data");
            Assert.NotNull(data);
            Assert.Equal(bytes, Convert.FromBase64String(data!));
            Assert.Equal(Path.GetFileName(path), GetString(result.Output.Items[0].Data, "fileName"));
            Assert.Equal("application/octet-stream", GetString(result.Output.Items[0].Data, "mimeType"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_TextMode_ReturnsDecodedText()
    {
        var text = "Hello, 世界 🌍";
        var bytes = Encoding.UTF8.GetBytes(text);
        var path = WriteTempFile(bytes, ".txt");

        try
        {
            var node = new ReadFileNode { Source = SourceOf(path), TextMode = true };

            var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

            Assert.True(result.Success, result.Error?.Message);
            Assert.Equal(text, GetString(result.Output.Items[0].Data, "data"));
            Assert.Equal("text/plain", GetString(result.Output.Items[0].Data, "mimeType"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_FileNotFound_ReturnsFileNotFound()
    {
        var path = Path.Combine(Path.GetTempPath(), $"readfile_missing_{Guid.NewGuid()}.bin");

        var node = new ReadFileNode { Source = SourceOf(path) };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("FileNotFound", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_EmptySource_ReturnsError()
    {
        var node = new ReadFileNode { Source = SourceOf("") };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("FileNotFound", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_NullSource_ReturnsError()
    {
        var node = new ReadFileNode { Source = null };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("FileNotFound", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_UrlSuccess_ReturnsBase64()
    {
        var bytes = new byte[] { 0x10, 0x20, 0x30 };
        using var client = new HttpClient(new ByteContentHandler(bytes));
        var pool = new StubHttpClientPool(client);

        var node = new ReadFileNode { Source = SourceOf("http://example.com/file.bin") };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch(), pool), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = GetString(result.Output.Items[0].Data, "data");
        Assert.NotNull(data);
        Assert.Equal(bytes, Convert.FromBase64String(data!));
    }

    [Fact]
    public async Task ExecuteAsync_UrlSsrfBlocked_ReturnsSsrfBlocked()
    {
        using var client = new HttpClient(new ByteContentHandler([1, 2, 3]));
        var pool = new StubHttpClientPool(client);

        var node = new ReadFileNode { Source = SourceOf("http://localhost/secret") };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch(), pool), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(FlowConstants.ErrorCodes.SsrfBlocked, result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_CustomBinaryField_UsesCustomFieldName()
    {
        var bytes = new byte[] { 0x42 };
        var path = WriteTempFile(bytes, ".bin");

        try
        {
            var node = new ReadFileNode { Source = SourceOf(path), BinaryField = "content" };

            var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

            Assert.True(result.Success, result.Error?.Message);
            var data = GetString(result.Output.Items[0].Data, "content");
            Assert.NotNull(data);
            Assert.Equal(bytes, Convert.FromBase64String(data!));
            Assert.Null(GetString(result.Output.Items[0].Data, "data"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_InvalidEncoding_ReturnsInvalidEncoding()
    {
        var bytes = Encoding.UTF8.GetBytes("plain text");
        var path = WriteTempFile(bytes, ".txt");

        try
        {
            var node = new ReadFileNode { Source = SourceOf(path), TextMode = true, Encoding = "!!!not-a-real-encoding" };

            var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("InvalidEncoding", result.Error?.Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UrlHttpClientUnavailable_ReturnsError()
    {
        var node = new ReadFileNode { Source = SourceOf("http://example.com/x") };

        // HttpClientPool 为 null：外部地址通过 SSRF 预检后应报客户端不可用。
        var result = await node.ExecuteAsync(CreateContext(new DataBatch(), httpClientPool: null), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(FlowConstants.ErrorCodes.HttpClientUnavailable, result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_SourceExpression_EvaluatedFromInput()
    {
        var bytes = new byte[] { 0x7F, 0x80 };
        var path = WriteTempFile(bytes, ".bin");

        try
        {
            // Source 为 JS 表达式，引用 $json.path，框架未预求值（无 ResolvedValue），依赖托管引擎求值。
            var node = new ReadFileNode
            {
                Source = new Script { Source = "$json.filePath", Language = ScriptLanguage.JavaScript, ReturnType = ScriptReturnType.String }
            };

            var context = CreateContext(
                new DataBatch
                {
                    Items =
                    [
                        new DataItem { Data = new JsonObject { ["filePath"] = path }, Success = true }
                    ]
                },
                httpClientPool: null);

            var result = await node.ExecuteAsync(context, CancellationToken.None);

            Assert.True(result.Success, result.Error?.Message);
            var data = GetString(result.Output.Items[0].Data, "data");
            Assert.NotNull(data);
            Assert.Equal(bytes, Convert.FromBase64String(data!));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempFile(byte[] bytes, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"readfile_{Guid.NewGuid()}{extension}");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string? GetString(JsonNode? node, string key)
        => node?[key] is JsonValue value ? value.GetValue<string>() : null;

    private static Script SourceOf(string source)
        => new Script
        {
            Source = $"'{source.Replace("\\", "\\\\").Replace("'", "\\'")}'",
            Language = ScriptLanguage.JavaScript,
            ReturnType = ScriptReturnType.String
        }.WithResolvedValue(JsonValue.Create(source));

    private static NodeExecutionContext CreateContext(DataBatch input, IHttpClientPool? httpClientPool = null)
    {
        return new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "test" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = "readFile",
                TypeName = "readFile",
                Name = "readFile"
            },
            Inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
            {
                [FlowConstants.PortNames.Input] = input
            },
            HttpClientPool = httpClientPool,
            ScriptCache = new ScriptCache(Options.Create(new JsEngineOptions())),
            EngineOptions = new JsEngineOptions(),
            CancellationToken = CancellationToken.None
        };
    }

    private sealed class StubHttpClientPool : IHttpClientPool
    {
        private readonly HttpClient _client;
        public StubHttpClientPool(HttpClient client) => _client = client;
        public HttpClient GetClient(string? name = null) => _client;
    }

    private sealed class ByteContentHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes;
        public ByteContentHandler(byte[] bytes) => _bytes = bytes;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_bytes)
            });
        }
    }
}
