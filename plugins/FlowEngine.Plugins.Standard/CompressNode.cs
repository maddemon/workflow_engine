using System.ComponentModel;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Tools;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 压缩/解压节点。对上游输入项 base64 字段中的二进制内容进行 Zip/Gzip/Tar 压缩与对应解压。
/// 二进制一律以 base64 内嵌于输出 JSON 字段（默认 <c>data</c>），不依赖任何附件存储后端
/// （本引擎当前无附件存储实现）。Zip/Gzip 使用 BCL <see cref="System.IO.Compression"/>；
/// Tar/Untar 为最小化 POSIX ustar（无压缩、单常规文件、短名）手写实现，避免引入第三方依赖。
/// </summary>
public sealed class CompressNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "compress";

    /// <inheritdoc />
    public string DisplayName => "Compress";

    /// <inheritdoc />
    public string Category => "Storage";

    /// <inheritdoc />
    public string Icon => "compress";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 压缩或解压操作。
    /// </summary>
    [Description("Compression or decompression operation.")]
    public CompressOperation Operation { get; set; } = CompressOperation.Zip;

    /// <summary>
    /// 输入 JSON 字段名，承载 base64 内容（压缩时为原文；解压时为归档）。默认 <c>data</c>。
    /// </summary>
    [Description("Input JSON field holding base64 content (source for zip/gzip/tar; archive for unzip/gunzip/untar). Default 'data'.")]
    public string InputField { get; set; } = "data";

    /// <summary>
    /// 输出 JSON 字段名，承载结果 base64。默认 <c>data</c>。
    /// </summary>
    [Description("Output JSON field name for the result base64. Default 'data'.")]
    public string OutputField { get; set; } = "data";

    /// <summary>
    /// 归档内单条目的文件名（压缩时写入；解压时多条目各取归档内条目名）。默认 <c>content</c>。
    /// </summary>
    [Description("Entry file name inside zip/tar archives. Default 'content'.")]
    public string EntryName { get; set; } = "content";

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
            // OnceForAll：仅取首个输入项；缺项或字段缺失/非字符串/null 均视为缺输入。
            var inputBatch = context.GetInputBatch();
            var item = inputBatch.Items.Count > 0 ? inputBatch.Items[0].Data : null;
            if (item is null)
            {
                return context.ErrorResult("MissingInput", "No input item available to read base64 content from.");
            }
            // Read base64 from the given field and decode; missing field or invalid base64 both fall back to MissingInput.
            byte[] bytes;
            var status = NodeDataHelpers.TryGetBase64Field(item, InputField, out bytes);
            if (status is not NodeDataHelpers.Base64FieldResult.Success)
            {
                return status == NodeDataHelpers.Base64FieldResult.Invalid
                    ? context.ErrorResult("MissingInput", $"Field '{InputField}' is not valid base64.")
                    : context.ErrorResult("MissingInput", $"Input item is missing a string value at field '{InputField}'.");
            }

            // 条目名：缺省回退 "content"（用于 zip/tar 单条目名称）。
            var entryName = string.IsNullOrEmpty(EntryName) ? "content" : EntryName;

            return Operation switch
            {
                CompressOperation.Zip => Zip(context, bytes, entryName, OutputField),
                CompressOperation.Unzip => Unzip(context, bytes, OutputField),
                CompressOperation.Gzip => Gzip(context, bytes, OutputField),
                CompressOperation.Gunzip => Gunzip(context, bytes, OutputField),
                CompressOperation.Tar => Tar(context, bytes, entryName, OutputField),
                CompressOperation.Untar => Untar(context, bytes, OutputField),
                _ => context.ErrorResult("InvalidOperation", $"Unsupported operation: {Operation}.")
            };
        }
        catch (OperationCanceledException)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.Cancelled, "compress was cancelled.");
        }
        catch (InvalidDataException ex)
        {
            // BCL 在解析损坏的 zip/gz/tar 时抛出。
            context.Logger?.LogWarning("compress 解析损坏归档失败：{Message}。", ex.Message);
            return context.ErrorResult("CorruptArchive", $"Archive is corrupt or not in the expected format: {ex.Message}");
        }
        catch (Exception ex)
        {
            context.Logger?.LogError(ex, "compress 执行出错。");
            return context.ErrorResult(FlowConstants.ErrorCodes.UnexpectedError, $"Unexpected error compressing/decompressing: {ex.Message}");
        }
    }

    // ---- Zip / Unzip ----

    private static NodeExecutionResult Zip(NodeExecutionContext context, byte[] source, string entryName, string outputField)
    {
        using var outStream = new MemoryStream();
        using (var archive = new ZipArchive(outStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var entryStream = entry.Open();
            entryStream.Write(source, 0, source.Length);
        }

        var zipBytes = outStream.ToArray();
        context.Logger?.LogInformation("compress Zip 已写入单条目 {Entry}（字节：{Bytes}）。", entryName, zipBytes.Length);

        var obj = new JsonObject
        {
            [outputField] = JsonValue.Create(Convert.ToBase64String(zipBytes)),
            ["fileName"] = JsonValue.Create(entryName + ".zip")
        };
        return context.Ok(obj);
    }

    private static NodeExecutionResult Unzip(NodeExecutionContext context, byte[] archiveBytes, string outputField)
    {
        var batch = new DataBatch();
        using var inStream = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(inStream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            using var entryStream = entry.Open();
            var entryBytes = ReadAll(entryStream);
            batch.Items.Add(new DataItem
            {
                Data = new JsonObject
                {
                    [outputField] = JsonValue.Create(Convert.ToBase64String(entryBytes)),
                    ["name"] = JsonValue.Create(entry.FullName)
                },
                Success = true,
                SourceIndex = batch.Items.Count
            });
        }

        context.Logger?.LogInformation("compress Unzip 解出 {Count} 个条目。", batch.Items.Count);
        return context.Ok(batch);
    }

    // ---- Gzip / Gunzip ----

    private static NodeExecutionResult Gzip(NodeExecutionContext context, byte[] source, string outputField)
    {
        using var outStream = new MemoryStream();
        using (var gzip = new GZipStream(outStream, CompressionMode.Compress, leaveOpen: true))
        {
            gzip.Write(source, 0, source.Length);
        }

        var gzipBytes = outStream.ToArray();
        context.Logger?.LogInformation("compress Gzip 完成（字节：{Bytes}）。", gzipBytes.Length);

        var obj = new JsonObject
        {
            [outputField] = JsonValue.Create(Convert.ToBase64String(gzipBytes))
        };
        return context.Ok(obj);
    }

    private static NodeExecutionResult Gunzip(NodeExecutionContext context, byte[] gzipBytes, string outputField)
    {
        using var inStream = new MemoryStream(gzipBytes, writable: false);
        using var gzip = new GZipStream(inStream, CompressionMode.Decompress);
        var outBytes = ReadAll(gzip);

        context.Logger?.LogInformation("compress Gunzip 完成（字节：{Bytes}）。", outBytes.Length);

        var obj = new JsonObject
        {
            [outputField] = JsonValue.Create(Convert.ToBase64String(outBytes))
        };
        return context.Ok(obj);
    }

    // ---- Tar / Untar (minimal POSIX ustar, uncompressed) ----

    private const int UstarBlockSize = 512;
    private const int UstarMaxNameLength = 100;

    private static NodeExecutionResult Tar(NodeExecutionContext context, byte[] source, string entryName, string outputField)
    {
        if (entryName.Length > UstarMaxNameLength)
        {
            return context.ErrorResult("EntryNameTooLong", $"Tar entry name exceeds {UstarMaxNameLength} characters: '{entryName}'.");
        }

        using var outStream = new MemoryStream();
        WriteUstarHeader(outStream, entryName, source.Length);
        outStream.Write(source, 0, source.Length);

        // 内容按 512 字节块对齐填充。
        var padding = (UstarBlockSize - (source.Length % UstarBlockSize)) % UstarBlockSize;
        if (padding > 0)
        {
            outStream.Write(new byte[padding], 0, padding);
        }

        var tarBytes = outStream.ToArray();
        context.Logger?.LogInformation("compress Tar 已写入单条目 {Entry}（字节：{Bytes}）。", entryName, tarBytes.Length);

        var obj = new JsonObject
        {
            [outputField] = JsonValue.Create(Convert.ToBase64String(tarBytes)),
            ["fileName"] = JsonValue.Create(entryName + ".tar")
        };
        return context.Ok(obj);
    }

    private static NodeExecutionResult Untar(NodeExecutionContext context, byte[] tarBytes, string outputField)
    {
        var batch = new DataBatch();
        using var inStream = new MemoryStream(tarBytes, writable: false);

        while (true)
        {
            var header = ReadBlock(inStream);
            if (header is null)
            {
                break; // 连续零块 = 归档结束。
            }

            ValidateUstarMagic(header);

            var name = ReadNullTerminatedAscii(header, 0, 100);
            var size = ParseOctal(header, 124, 12);
            var typeFlag = header[156];

            if (typeFlag == (byte)'0')
            {
                var content = new byte[size];
                var read = inStream.Read(content, 0, content.Length);
                if (read != content.Length)
                {
                    throw new InvalidDataException("Unexpected end of stream while reading tar entry content.");
                }

                // 内容按 512 字节块跳过填充。
                var padding = (UstarBlockSize - (size % UstarBlockSize)) % UstarBlockSize;
                if (padding > 0)
                {
                    inStream.Seek(padding, SeekOrigin.Current);
                }

                batch.Items.Add(new DataItem
                {
                    Data = new JsonObject
                    {
                        [outputField] = JsonValue.Create(Convert.ToBase64String(content)),
                        ["name"] = JsonValue.Create(name)
                    },
                    Success = true,
                    SourceIndex = batch.Items.Count
                });
            }
            else
            {
                // 忽略目录/长名/特殊文件等不支持的块：跳过内容即可。
                var padding = (UstarBlockSize - (size % UstarBlockSize)) % UstarBlockSize;
                if (size + padding > 0)
                {
                    inStream.Seek(size + padding, SeekOrigin.Current);
                }
            }
        }

        context.Logger?.LogInformation("compress Untar 解出 {Count} 个常规文件条目。", batch.Items.Count);
        return context.Ok(batch);
    }

    /// <summary>
    /// 读取一个 512 字节块；遇到全零块（归档结束标记）或流结束返回 null；部分/非对齐数据视为损坏。
    /// </summary>
    private static byte[]? ReadBlock(Stream stream)
    {
        var block = new byte[UstarBlockSize];
        var offset = 0;
        while (offset < UstarBlockSize)
        {
            var n = stream.Read(block, offset, UstarBlockSize - offset);
            if (n == 0)
            {
                break;
            }

            offset += n;
        }

        if (offset == 0)
        {
            return null;
        }

        if (offset < UstarBlockSize)
        {
            // 读取到部分数据但未填满一个块：非对齐，判定损坏。
            throw new InvalidDataException("Unexpected end of stream in the middle of a tar header block.");
        }

        // 整块为零：归档结束。
        var allZero = true;
        for (var i = 0; i < UstarBlockSize; i++)
        {
            if (block[i] != 0)
            {
                allZero = false;
                break;
            }
        }

        return allZero ? null : block;
    }

    private static void WriteUstarHeader(Stream stream, string name, long size)
    {
        var header = new byte[UstarBlockSize];

        var nameBytes = Encoding.ASCII.GetBytes(name);
        if (nameBytes.Length > UstarMaxNameLength)
        {
            throw new InvalidDataException("Entry name too long for ustar.");
        }

        Array.Copy(nameBytes, 0, header, 0, nameBytes.Length);

        // mode / uid / gid：八进制 0644/0/0，NUL 结尾（0o644 = 十进制 420）。
        WriteOctalField(header, 100, 8, 420);
        WriteOctalField(header, 108, 8, 0);
        WriteOctalField(header, 116, 8, 0);

        // size：12 字节域，11 位八进制 + NUL。
        WriteOctalField(header, 124, 12, size);

        // mtime：与 size 同格式。
        WriteOctalField(header, 136, 12, 0);

        // typeflag：'0' = 常规文件。
        header[156] = (byte)'0';

        // magic + version："ustar\0" + "00"。
        Encoding.ASCII.GetBytes("ustar").CopyTo(header, 257);
        header[263] = (byte)'0';
        header[264] = (byte)'0';

        // uname / gname："root\0"。
        Encoding.ASCII.GetBytes("root").CopyTo(header, 265);
        Encoding.ASCII.GetBytes("root").CopyTo(header, 297);

        // devmajor / devmajor：八进制 0。
        WriteOctalField(header, 329, 8, 0);
        WriteOctalField(header, 337, 8, 0);

        // 校验和：先以空格填充校验和域再求和，最后写入 6 位八进制 + NUL + 空格。
        for (var i = 148; i < 156; i++)
        {
            header[i] = (byte)' ';
        }

        var sum = 0;
        foreach (var b in header)
        {
            sum += b;
        }

        var checksumString = $"{sum:000000}\0 ";
        Encoding.ASCII.GetBytes(checksumString).CopyTo(header, 148);

        stream.Write(header, 0, header.Length);
    }

    private static void ValidateUstarMagic(byte[] header)
    {
        // magic 域（偏移 257，6 字节）应为 "ustar\0"。
        if (header[257] != (byte)'u' || header[258] != (byte)'s' || header[259] != (byte)'t'
            || header[260] != (byte)'a' || header[261] != (byte)'r' || header[262] != 0)
        {
            throw new InvalidDataException("Tar header is missing the ustar magic; not a valid archive.");
        }
    }

    private static void WriteOctalField(byte[] header, int offset, int length, long value)
    {
        // 域格式：靠右的八进制数字 + NUL 结尾（长度 - 1 位数字 + 1 个 NUL）。
        var digits = Convert.ToString(value, 8);
        var digitLength = length - 1;
        if (digits.Length > digitLength)
        {
            digits = digits[^(digitLength)..];
        }

        Encoding.ASCII.GetBytes(digits).CopyTo(header, offset + (digitLength - digits.Length));
        header[offset + digitLength] = 0;
    }

    private static long ParseOctal(byte[] header, int offset, int length)
    {
        var sb = new StringBuilder(length);
        for (var i = offset; i < offset + length; i++)
        {
            var c = header[i];
            // 跳过左填充的 NUL / 空格字节（ustar 八进制域靠右对齐）。
            if (sb.Length == 0 && (c == 0 || c == ' '))
            {
                continue;
            }

            if (c == 0 || c == ' ')
            {
                break;
            }

            sb.Append((char)c);
        }

        return sb.Length == 0 ? 0 : Convert.ToInt64(sb.ToString(), 8);
    }

    private static string ReadNullTerminatedAscii(byte[] header, int offset, int maxLength)
    {
        var end = offset;
        while (end < offset + maxLength && header[end] != 0)
        {
            end++;
        }

        return Encoding.ASCII.GetString(header, offset, end - offset);
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}

/// <summary>
/// compress 节点支持的压缩 / 解压操作。
/// </summary>
public enum CompressOperation
{
    /// <summary>将输入内容压缩为单个条目的 zip 归档。</summary>
    [Description("Compress the input into a single-entry zip archive.")]
    Zip,

    /// <summary>解压 zip 归档，每个条目输出一个数据项。</summary>
    [Description("Decompress a zip archive; one output item per entry.")]
    Unzip,

    /// <summary>将输入内容压缩为 gzip 流。</summary>
    [Description("Compress the input into a gzip stream.")]
    Gzip,

    /// <summary>解压 gzip 流还原原文。</summary>
    [Description("Decompress a gzip stream back to the original bytes.")]
    Gunzip,

    /// <summary>将输入内容打包为最小化 ustar tar 归档（无压缩）。</summary>
    [Description("Pack the input into a minimal (uncompressed) ustar tar archive.")]
    Tar,

    /// <summary>解包 ustar tar 归档，每个常规文件条目输出一个数据项。</summary>
    [Description("Unpack a ustar tar archive; one output item per regular file entry.")]
    Untar
}
