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
[NodeMeta(TypeName = "writeFile", DisplayName = "Write File", Category = NodeCategory.Storage, Icon = "file-write", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class WriteFileNode : NodeBase
{
    [Inject] public NodeExecutionContext Ctx { get; private set; } = null!;
    [Inject] public IExecutionLogger? Logger { get; private set; }
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
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        try
        {
            // 缺输入项即视为缺内容（OnceForAll 仅取首个输入项）。
            var inputBatch = input.InputBatch;
            var item = inputBatch.Items.Count > 0 ? inputBatch.Items[0].Data : null;
            if (item is null)
            {
                throw new NodeExecutionException("MissingBinaryField", "No input item available to read base64 content from.");
            }

            if (FileName is null)
            {
                throw new NodeExecutionException("MissingFileName", "FileName is required.");
            }

            var fileName = await FileName.EvaluateAsync<string>(Ctx, item: item, itemIndex: 0, cancellationToken: ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new NodeExecutionException("MissingFileName", "FileName must evaluate to a non-empty string.");
            }

            // 从指定字段取 base64 字符串；缺失或非字符串均视为缺内容。
            byte[] bytes;
            var status = NodeDataHelpers.TryGetBase64Field(item, BinaryField, out bytes);
            if (status is not NodeDataHelpers.Base64FieldResult.Success)
            {
                throw status == NodeDataHelpers.Base64FieldResult.Invalid
                    ? new NodeExecutionException("InvalidBase64", "Field content is not valid base64.")
                    : new NodeExecutionException("MissingBinaryField", $"Input item is missing a string value at field '{BinaryField}'.");
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
                    await File.AppendAllBytesAsync(target, bytes, ct).ConfigureAwait(false);
                }
                else
                {
                    await File.WriteAllBytesAsync(target, bytes, ct).ConfigureAwait(false);
                }
            }
            catch (UnauthorizedAccessException)
            {
                Logger?.LogWarning("writeFile 无权限写入 {FileName}。", fileName);
                throw new NodeExecutionException("WriteError", $"Access denied writing file: '{fileName}'.");
            }
            catch (IOException)
            {
                Logger?.LogWarning("writeFile 写入 {FileName} 发生 IO 错误。", fileName);
                throw new NodeExecutionException("WriteError", $"Failed to write file: '{fileName}'.");
            }

            Logger?.LogInformation("writeFile 已写入 {FileName}（模式：{Mode}，字节：{Bytes}）。", fileName, WriteMode, bytes.Length);

            return NodeHandlerOutput.Data(new DataBatch
            {
                Items =
                [
                    new DataItem
                    {
                        Data = new JsonObject
                        {
                            ["filePath"] = JsonValue.Create(System.IO.Path.GetFullPath(target)),
                            ["fileName"] = JsonValue.Create(fileName),
                            ["bytesWritten"] = JsonValue.Create(bytes.Length)
                        },
                        Success = true,
                        SourceIndex = 0
                    }
                ]
            });
        }
        catch (OperationCanceledException)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.Cancelled, "writeFile was cancelled.");
        }
        catch (ScriptErrorException ex)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.ScriptError, $"FileName expression evaluation failed: {ex.Message}");
        }
        catch (Exception ex) when (ex is not NodeExecutionException)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.UnexpectedError, $"Unexpected error writing file: {ex.Message}");
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