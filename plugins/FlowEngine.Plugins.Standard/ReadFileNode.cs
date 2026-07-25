using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 文件读取节点。从本地磁盘路径或 http(s) URL 读取二进制内容，嵌入输出项的 JSON 字段中：
/// 二进制模式存 base64 字符串，文本模式解码为字符串。二进制一律以 base64/文本内嵌于 <c>Data</c>，
/// 不依赖任何附件存储后端（本引擎当前无附件存储实现）。
/// </summary>
public sealed class ReadFileNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "readFile";

    /// <inheritdoc />
    public string DisplayName => "Read File";

    /// <inheritdoc />
    public string Category => "Storage";

    /// <inheritdoc />
    public string Icon => "file-read";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 文件来源：本地磁盘路径或 http(s) URL。JS 表达式，支持 <c>$json</c> / <c>$input</c> 注入参数。必填。
    /// </summary>
    [Hint(PresentationHint.Script)]
    [Description("File path or http(s) URL. JS expression; supports $json/$input.")]
    public Script? Source { get; set; }

    /// <summary>
    /// 输出 JSON 字段名，用于承载文件内容（二进制模式为 base64，文本模式为原始文本）。默认 <c>data</c>。
    /// </summary>
    [Description("Output JSON field name holding the file content (base64 in binary mode, raw text in text mode).")]
    public string BinaryField { get; set; } = "data";

    /// <summary>
    /// 文本模式开关。为 <c>true</c> 时将字节解码为文本并存储字符串；为 <c>false</c> 时存储 base64。
    /// </summary>
    [Description("When true, decode bytes to text and store the string; when false, store base64.")]
    public bool TextMode { get; set; }

    /// <summary>
    /// 文本模式使用的字符编码名称。默认 <c>utf-8</c>。非法编码名将返回 <c>InvalidEncoding</c> 错误。
    /// </summary>
    [Description("Text encoding used when TextMode is true. Default utf-8.")]
    public string Encoding { get; set; } = "utf-8";

    /// <inheritdoc />
    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new PortDefinition { Name = FlowConstants.PortNames.Input, DisplayName = "Input", Direction = PortDirection.Input, Type = PortType.Main },
        new PortDefinition { Name = FlowConstants.PortNames.Output, DisplayName = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    /// <inheritdoc />
    public bool DefaultIsEntry => false;

    /// <inheritdoc />
    public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (Source is null)
            {
                return context.ErrorResult("FileNotFound", "Source is required.");
            }

            // 支持 $json/$input 注入：以首个输入项作为求值作用域（OnceForAll 仅读取单个来源）。
            var inputBatch = context.GetInputBatch();
            var firstItem = inputBatch.Items.Count > 0 ? inputBatch.Items[0].Data : null;

            var source = await Source.EvaluateAsync<string>(context, firstItem, 0, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(source))
            {
                return context.ErrorResult("FileNotFound", "Source is required.");
            }

            byte[]? bytes = null;
            string? base64 = null;
            string? fileName = Path.GetFileName(source);
            string mime;

            // URL 来源：复用 GuardSsrf 做 SSRF 预检与 HttpClientPool 取客户端，不重复 HTTP 认证/SSRF 逻辑。
            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var ssrfBlock = context.GuardSsrf(source);
                if (ssrfBlock is not null)
                {
                    return ssrfBlock;
                }

                var client = context.HttpClientPool?.GetClient();
                if (client is null)
                {
                    return context.ErrorResult(FlowConstants.ErrorCodes.HttpClientUnavailable, "HTTP client pool is not configured.");
                }

                using var response = await client.GetAsync(source, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return context.ErrorResult("UrlFetchError",
                        $"Failed to fetch '{fileName}' (status {(int)response.StatusCode} {response.StatusCode}).");
                }

                bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                mime = response.Content.Headers.ContentType?.MediaType ?? InferMime(fileName);
            }
            else
            {
                // 本地文件：异步读取，捕获缺文件/无权限/IO 异常统一转为 FileNotFound。
                // Local file: stream read. Binary mode encodes via CryptoStream+ToBase64Transform
                // incrementally to avoid holding the whole byte array in memory.
                try
                {
                    using var fileStream = File.OpenRead(source);
                    if (TextMode)
                    {
                        using var buffer = new MemoryStream();
                        await fileStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                        bytes = buffer.ToArray();
                    }
                    else
                    {
                        using var base64Stream = new CryptoStream(fileStream, new ToBase64Transform(), CryptoStreamMode.Read);
                        using var base64Reader = new StreamReader(base64Stream, System.Text.Encoding.ASCII);
                        base64 = await base64Reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (FileNotFoundException)
                {
                    context.Logger?.LogWarning("readFile 未找到文件 {FileName}。", fileName);
                    return context.ErrorResult("FileNotFound", $"File not found: '{fileName}'.");
                }
                catch (UnauthorizedAccessException)
                {
                    context.Logger?.LogWarning("readFile 无权限读取 {FileName}。", fileName);
                    return context.ErrorResult("FileNotFound", $"Access denied reading file: '{fileName}'.");
                }
                catch (IOException)
                {
                    context.Logger?.LogWarning("readFile 读取 {FileName} 发生 IO 错误。", fileName);
                    return context.ErrorResult("FileNotFound", $"Failed to read file: '{fileName}'.");
                }

                mime = InferMime(fileName);
            }

            context.Logger?.LogInformation("readFile 已读取 {FileName}（模式：{Mode}）。", fileName, TextMode ? "text" : "binary");

            // 文本模式：将字节解码为字符串；编码名非法时返回 InvalidEncoding。
            JsonNode content;
            if (TextMode)
            {
                Encoding enc;
                try
                {
                    enc = System.Text.Encoding.GetEncoding(Encoding);
                }
                catch (ArgumentException)
                {
                    return context.ErrorResult("InvalidEncoding", $"Invalid text encoding: '{Encoding}'.");
                }

                content = JsonValue.Create(enc.GetString(bytes!));
            }
            else
            {
                content = base64 is not null
                    ? JsonValue.Create(base64)
                    : JsonValue.Create(Convert.ToBase64String(bytes!));
            }

            var obj = new JsonObject
            {
                [BinaryField] = content,
                ["fileName"] = JsonValue.Create(fileName),
                ["mimeType"] = JsonValue.Create(mime)
            };

            return context.Ok(obj);
        }
        catch (OperationCanceledException)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.Cancelled, "readFile was cancelled.");
        }
        catch (ScriptErrorException ex)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.ScriptError, $"Source expression evaluation failed: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            return context.ErrorResult("UrlFetchError", $"HTTP request failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.UnexpectedError, $"Unexpected error reading file: {ex.Message}");
        }
    }

    /// <summary>
    /// 按文件扩展名简单推断 MIME 类型；未知扩展名回退为 <c>application/octet-stream</c>。
    /// </summary>
    private static string InferMime(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return "application/octet-stream";
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".csv" => "text/csv",
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".zip" => "application/zip",
            ".gz" => "application/gzip",
            ".mp3" => "audio/mpeg",
            ".mp4" => "video/mp4",
            ".md" => "text/markdown",
            ".yaml" or ".yml" => "application/yaml",
            _ => "application/octet-stream"
        };
    }
}
