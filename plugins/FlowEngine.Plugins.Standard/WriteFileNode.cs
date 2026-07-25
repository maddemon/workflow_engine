using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;
using FlowEngine.Core.Tools;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 写文件节点。从上游输入项的 base64 字段解码出二进制内容并落盘（覆盖或追加）。
/// 二进制一律从 base64 字段解码写入本地磁盘，不依赖任何附件存储后端（本引擎当前无附件存储实现）。
/// </summary>
public sealed class WriteFileNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "writeFile";

    /// <inheritdoc />
    public string DisplayName => "Write File";

    /// <inheritdoc />
    public string Category => "Storage";

    /// <inheritdoc />
    public string Icon => "file-write";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 输入 JSON 字段名，承载待写入的 base64 内容。默认 <c>data</c>。
    /// </summary>
    [Description("Input JSON field holding base64 content to write. Default 'data'.")]
    public string BinaryField { get; set; } = "data";

    /// <summary>
    /// 输出文件名（或相对路径）。JS 表达式，支持 <c>$json</c> / <c>$input</c> 注入参数。必填。
    /// </summary>
    [Hint(PresentationHint.Script)]
    [Description("Output file name (or relative path). JS expression; supports $json/$input.")]
    public Script? FileName { get; set; }

    /// <summary>
    /// 可选基础目录。设置时，<see cref="FileName"/> 视为相对其的相对路径；目录不存在时自动创建。
    /// </summary>
    [Description("Optional base directory. When set, FileName is treated as relative to it; the directory is created if missing.")]
    public string? Path { get; set; }

    /// <summary>
    /// 写入模式：<see cref="FileWriteMode.Overwrite"/> 截断覆写；<see cref="FileWriteMode.Append"/> 追加到文件尾部。
    /// </summary>
    [Description("Overwrite truncates; Append concatenates to existing file.")]
    public FileWriteMode WriteMode { get; set; } = FileWriteMode.Overwrite;

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
            // 缺输入项即视为缺内容（OnceForAll 仅取首个输入项）。
            var inputBatch = context.GetInputBatch();
            var item = inputBatch.Items.Count > 0 ? inputBatch.Items[0].Data : null;
            if (item is null)
            {
                return context.ErrorResult("MissingBinaryField", "No input item available to read base64 content from.");
            }

            if (FileName is null)
            {
                return context.ErrorResult("MissingFileName", "FileName is required.");
            }

            var fileName = await FileName.EvaluateAsync<string>(context, item, 0, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return context.ErrorResult("MissingFileName", "FileName must evaluate to a non-empty string.");
            }

            // 从指定字段取 base64 字符串；缺失或非字符串均视为缺内容。
            byte[] bytes;
            var status = NodeDataHelpers.TryGetBase64Field(item, BinaryField, out bytes);
            if (status is not NodeDataHelpers.Base64FieldResult.Success)
            {
                return status == NodeDataHelpers.Base64FieldResult.Invalid
                    ? context.ErrorResult("InvalidBase64", "Field content is not valid base64.")
                    : context.ErrorResult("MissingBinaryField", $"Input item is missing a string value at field '{BinaryField}'.");
            }

            // 解析目标路径：Path 非空时自动创建目录并按相对路径组合。
            string target;
            if (!string.IsNullOrWhiteSpace(Path))
            {
                Directory.CreateDirectory(Path);
                target = System.IO.Path.Combine(Path, fileName!);
            }
            else
            {
                target = fileName!;
            }

            try
            {
                if (WriteMode == FileWriteMode.Append)
                {
                    await File.AppendAllBytesAsync(target, bytes, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await File.WriteAllBytesAsync(target, bytes, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (UnauthorizedAccessException)
            {
                context.Logger?.LogWarning("writeFile 无权限写入 {FileName}。", fileName);
                return context.ErrorResult("WriteError", $"Access denied writing file: '{fileName}'.");
            }
            catch (IOException)
            {
                context.Logger?.LogWarning("writeFile 写入 {FileName} 发生 IO 错误。", fileName);
                return context.ErrorResult("WriteError", $"Failed to write file: '{fileName}'.");
            }

            context.Logger?.LogInformation("writeFile 已写入 {FileName}（模式：{Mode}，字节：{Bytes}）。", fileName, WriteMode, bytes.Length);

            var obj = new JsonObject
            {
                ["filePath"] = JsonValue.Create(System.IO.Path.GetFullPath(target)),
                ["fileName"] = JsonValue.Create(fileName),
                ["bytesWritten"] = JsonValue.Create(bytes.Length)
            };

            return context.Ok(obj);
        }
        catch (OperationCanceledException)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.Cancelled, "writeFile was cancelled.");
        }
        catch (ScriptErrorException ex)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.ScriptError, $"FileName expression evaluation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.UnexpectedError, $"Unexpected error writing file: {ex.Message}");
        }
    }
}

/// <summary>
/// 写文件节点的写入模式。
/// </summary>
public enum FileWriteMode
{
    /// <summary>截断并覆盖已有文件（不存在则创建）。</summary>
    [Description("Truncate and overwrite the existing file.")]
    Overwrite,

    /// <summary>追加到已有文件尾部（不存在则创建）。</summary>
    [Description("Append to the end of the existing file; create if not exists.")]
    Append
}
