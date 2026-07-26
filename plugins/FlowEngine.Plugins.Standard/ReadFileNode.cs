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
using FlowEngine.Core.Http;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 文件读取节点。从本地磁盘路径或 http(s) URL 读取二进制内容，嵌入输出项的 JSON 字段中：
/// 二进制模式存 base64 字符串，文本模式解码为字符串。二进制一律以 base64/文本内嵌于 <c>Data</c>，
/// 不依赖任何附件存储后端（本引擎当前无附件存储实现）。
/// </summary>
[NodeMeta(TypeName = "readFile", DisplayName = "Read File", Category = NodeCategory.Storage, Icon = "file-read", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class ReadFileNode : NodeBase
{
    private static readonly HttpExecutionService HttpService = new HttpExecutionService();

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
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        try
        {
            if (Source is null)
            {
                throw new NodeExecutionException("FileNotFound", "Source is required.");
            }

            // 支持 $json/$input 注入：以首个输入项作为求值作用域（OnceForAll 仅读取单个来源）。
            var inputBatch = input.InputBatch;
            var evalItem = inputBatch.Items.Count > 0 ? inputBatch.Items[0].Data : null;

            var source = await EvaluateItemAsync<string>(Source, evalItem, 0, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new NodeExecutionException("FileNotFound", "Source is required.");
            }

            byte[]? bytes = null;
            string? base64 = null;
            string fileName = Path.GetFileName(source);
            string mime;

            // URL 来源：复用 GuardSsrf 做 SSRF 预检，并经 HttpExecutionService 取内容（复用其客户端池/异常处理）。
            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var ssrfBlock = GuardSsrf(source);
                if (ssrfBlock is not null)
                {
                    throw new NodeExecutionException(ssrfBlock.Error!.Code, ssrfBlock.Error.Message);
                }

                var httpResult = await HttpService.ExecuteAsync(
                    new HttpExecutionRequest { Url = source, Method = HttpMethod.Get },
                    ExecutionContext,
                    ct).ConfigureAwait(false);

                if (!httpResult.Success)
                {
                    throw new NodeExecutionException(httpResult.Error!.Code, httpResult.Error.Message);
                }

                // HttpExecutionService 的响应体位于输出信封的 .body 下；二进制内容在此以字符串承载，
                // 取字符串的 UTF-8 字节表示（与历史 ReadAsByteArrayAsync 在 <0x80 字节上等价，保持下游编码/输出不变）。
                var envelope = httpResult.Output.Items.Count > 0 ? httpResult.Output.Items[0].Data as JsonObject : null;
                var bodyNode = envelope?["body"];
                var bodyText = bodyNode switch
                {
                    JsonValue jv => jv.GetValue<string>(),
                    null => string.Empty,
                    _ => bodyNode.ToJsonString()
                };
                bytes = System.Text.Encoding.UTF8.GetBytes(bodyText);
                mime = InferMime(fileName);
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
                        await fileStream.CopyToAsync(buffer, ct).ConfigureAwait(false);
                        bytes = buffer.ToArray();
                    }
                    else
                    {
                        using var base64Stream = new CryptoStream(fileStream, new ToBase64Transform(), CryptoStreamMode.Read);
                        using var base64Reader = new StreamReader(base64Stream, System.Text.Encoding.ASCII);
                        base64 = await base64Reader.ReadToEndAsync(ct).ConfigureAwait(false);
                    }
                }
                catch (FileNotFoundException)
                {
                    Logger?.LogWarning("readFile 未找到文件 {FileName}。", fileName);
                    throw new NodeExecutionException("FileNotFound", $"File not found: '{fileName}'.");
                }
                catch (UnauthorizedAccessException)
                {
                    Logger?.LogWarning("readFile 无权限读取 {FileName}。", fileName);
                    throw new NodeExecutionException("FileNotFound", $"Access denied reading file: '{fileName}'.");
                }
                catch (IOException)
                {
                    Logger?.LogWarning("readFile 读取 {FileName} 发生 IO 错误。", fileName);
                    throw new NodeExecutionException("FileNotFound", $"Failed to read file: '{fileName}'.");
                }

                mime = InferMime(fileName);
            }

            Logger?.LogInformation("readFile 已读取 {FileName}（模式：{Mode}）。", fileName, TextMode ? "text" : "binary");

            // 文本模式：将字节解码为字符串；编码名非法时返回 InvalidEncoding。
            JsonNode content;
            if (TextMode)
            {
                System.Text.Encoding enc;
                try
                {
                    enc = System.Text.Encoding.GetEncoding(Encoding);
                }
                catch (ArgumentException)
                {
                    throw new NodeExecutionException("InvalidEncoding", $"Invalid text encoding: '{Encoding}'.");
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

            return Single(obj);
        }
        catch (OperationCanceledException)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.Cancelled, "readFile was cancelled.");
        }
        catch (ScriptErrorException ex)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.ScriptError, $"Source expression evaluation failed: {ex.Message}");
        }
        catch (Exception ex) when (ex is not NodeExecutionException)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.UnexpectedError, $"Unexpected error reading file: {ex.Message}");
        }
    }

    /// <summary>
    /// 构造单数据项的成功输出。
    /// </summary>
    private static NodeHandlerOutput Single(JsonNode? data) =>
        NodeHandlerOutput.Data(new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = data,
                    Success = true,
                    SourceIndex = 0
                }
            ]
        });

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
