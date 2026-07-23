using System.ComponentModel;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Tools;
using MiniExcelLibs;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 表格节点。读取 CSV / XLSX / ODS 文件并输出每行一个 <see cref="DataItem"/>（列→JSON 值），
/// 或将上游 <see cref="DataBatch"/> 的每行写为表格文件。
/// 二进制一律以 base64 内嵌于输出 JSON 字段（默认 <c>data</c>），不依赖任何附件存储后端
/// （本引擎当前无附件存储实现）；读取时可从本地 <c>FilePath</c> 读，或从上游 base64 字段（<c>InputField</c>）读。
/// CSV 与 XLSX 使用内置解析/写入器与 MiniExcel；ODS 因 MiniExcel 1.45.0 不支持，
/// 改用内置 BCL（<c>ZipArchive</c> + LINQ-to-XML）实现。
/// </summary>
public sealed class SpreadsheetNode : INodeType
{
    /// <inheritdoc />
    public string TypeName => "spreadsheet";

    /// <inheritdoc />
    public string DisplayName => "Spreadsheet";

    /// <inheritdoc />
    public string Category => "File";

    /// <inheritdoc />
    public string Icon => "spreadsheet";

    /// <inheritdoc />
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    /// <summary>
    /// 操作：将表格读入 <see cref="DataBatch"/>，或将 <see cref="DataBatch"/> 写为表格文件。
    /// </summary>
    [Description("Read a spreadsheet into a DataBatch, or write a DataBatch into a spreadsheet file.")]
    public SpreadsheetOperation Operation { get; set; } = SpreadsheetOperation.Read;

    /// <summary>
    /// 文件格式。CSV 使用内置解析器；XLSX 使用 MiniExcel；ODS 使用内置 BCL（ZipArchive + LINQ-to-XML）。
    /// </summary>
    [Description("File format. Csv uses built-in parser; Xlsx uses MiniExcel; Ods uses built-in BCL (ZipArchive + LINQ-to-XML).")]
    public SpreadsheetFormat Format { get; set; } = SpreadsheetFormat.Csv;

    /// <summary>
    /// 本地文件路径。Read 时为源文件（省略时回退到 <see cref="InputField"/> 的 base64）；
    /// Write 时为输出文件（提供时落盘；base64 仍写入 <see cref="OutputField"/>）。
    /// </summary>
    [Description("Local file path. Read: source file (falls back to InputField base64 when omitted). Write: output file (written when provided; base64 still emitted in OutputField).")]
    public string? FilePath { get; set; }

    /// <summary>
    /// 承载 base64 内容的输入 JSON 字段，Read 且 <see cref="FilePath"/> 为空时使用。默认 <c>data</c>。
    /// </summary>
    [Description("Input JSON field holding base64 content, used when FilePath is omitted on Read. Default 'data'.")]
    public string InputField { get; set; } = "data";

    /// <summary>
    /// 承载写入文件 base64 的输出 JSON 字段（Write）。默认 <c>data</c>。
    /// </summary>
    [Description("Output JSON field holding the written file's base64 (Write). Default 'data'.")]
    public string OutputField { get; set; } = "data";

    /// <summary>
    /// 工作表名称（XLSX 读/写）。省略时取第一个工作表。
    /// </summary>
    [Description("Worksheet name for Xlsx (Read/Write). When omitted, the first sheet is used.")]
    public string? SheetName { get; set; }

    /// <summary>
    /// 是否将首行视为表头（列名）。默认 <c>true</c>。
    /// </summary>
    [Description("Treat first row as header (column names). Default true.")]
    public bool HasHeader { get; set; } = true;

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
            return Operation switch
            {
                SpreadsheetOperation.Read => await ReadAsync(context, cancellationToken).ConfigureAwait(false),
                SpreadsheetOperation.Write => await WriteAsync(context, cancellationToken).ConfigureAwait(false),
                _ => context.ErrorResult("InvalidOperation", $"Unsupported operation: {Operation}.")
            };
        }
        catch (OperationCanceledException)
        {
            return context.ErrorResult(FlowConstants.ErrorCodes.Cancelled, "spreadsheet was cancelled.");
        }
        catch (InvalidDataException ex)
        {
            // 内置 CSV 解析器在字段未闭合时抛出；统一视为解析失败。
            context.Logger?.LogWarning("spreadsheet 解析失败：{Message}。", ex.Message);
            return context.ErrorResult("ParseError", $"Failed to parse spreadsheet: {ex.Message}");
        }
        catch (Exception ex)
        {
            context.Logger?.LogError(ex, "spreadsheet 执行出错。");
            return context.ErrorResult(FlowConstants.ErrorCodes.UnexpectedError, $"Unexpected error processing spreadsheet: {ex.Message}");
        }
    }

    // ---- Read ----

    private async Task<NodeExecutionResult> ReadAsync(NodeExecutionContext context, CancellationToken cancellationToken)
    {
        byte[] bytes;
        if (!string.IsNullOrEmpty(FilePath))
        {
            try
            {
                bytes = await File.ReadAllBytesAsync(FilePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                context.Logger?.LogWarning("spreadsheet 读取文件失败 {Path}：{Message}。", FilePath, ex.Message);
                return context.ErrorResult("ReadError", $"Failed to read file '{FilePath}': {ex.Message}");
            }
        }
        else
        {
            // 无 FilePath：从首个输入项的 InputField base64 读取。
            var inputBatch = context.GetInputBatch();
            var item = inputBatch.Items.Count > 0 ? inputBatch.Items[0].Data : null;
            if (item is null)
            {
                return context.ErrorResult("MissingInput", "No input item available to read base64 content from.");
            }
            // No FilePath: read base64 from the first input item's InputField and decode.
            var status = NodeDataHelpers.TryGetBase64Field(item, InputField, out bytes);
            if (status is not NodeDataHelpers.Base64FieldResult.Success)
            {
                return status == NodeDataHelpers.Base64FieldResult.Invalid
                    ? context.ErrorResult("MissingInput", $"Field '{InputField}' is not valid base64.")
                    : context.ErrorResult("MissingInput", $"Input item is missing a string value at field '{InputField}'.");
            }
        }

        return Format switch
        {
            SpreadsheetFormat.Csv => ReadCsv(context, bytes, HasHeader),
            SpreadsheetFormat.Xlsx => ReadWorkbook(context, bytes, HasHeader, SheetName),
            SpreadsheetFormat.Ods => ReadOds(context, bytes, HasHeader, SheetName),
            _ => context.ErrorResult("InvalidFormat", $"Unsupported format: {Format}.")
        };
    }

    private static NodeExecutionResult ReadCsv(NodeExecutionContext context, byte[] bytes, bool hasHeader)
    {
        // 去除 UTF-8 BOM 后再按 UTF-8 解析文本。
        var text = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
            : Encoding.UTF8.GetString(bytes);

        List<List<string>> rows;
        try
        {
            rows = ParseCsv(text);
        }
        catch (InvalidDataException ex)
        {
            return context.ErrorResult("ParseError", ex.Message);
        }

        var batch = new DataBatch();
        if (rows.Count == 0)
        {
            return context.Ok(batch);
        }

        if (hasHeader)
        {
            var headers = rows[0];
            for (var r = 1; r < rows.Count; r++)
            {
                var obj = new JsonObject();
                var fields = rows[r];
                for (var c = 0; c < headers.Count; c++)
                {
                    var key = string.IsNullOrEmpty(headers[c]) ? $"col{c}" : headers[c];
                    obj[key] = c < fields.Count ? JsonValue.Create(fields[c]) : null;
                }

                AddItem(batch, obj);
            }
        }
        else
        {
            var colCount = 0;
            foreach (var row in rows)
            {
                colCount = Math.Max(colCount, row.Count);
            }

            foreach (var row in rows)
            {
                var obj = new JsonObject();
                for (var c = 0; c < colCount; c++)
                {
                    obj[$"col{c}"] = c < row.Count ? JsonValue.Create(row[c]) : null;
                }

                AddItem(batch, obj);
            }
        }

        context.Logger?.LogInformation("spreadsheet Read CSV 解析 {Count} 行（HasHeader={HasHeader}）。", batch.Items.Count, hasHeader);
        return context.Ok(batch);
    }

    private static NodeExecutionResult ReadWorkbook(NodeExecutionContext context, byte[] bytes, bool hasHeader, string? sheetName)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            // MiniExcel 返回 IEnumerable<dynamic>（ExpandoObject，实现 IDictionary<string,object>）；
            // 必须在流开启期间物化为字典列表。
            var rows = MiniExcel.Query(stream, useHeaderRow: hasHeader, sheetName: sheetName, excelType: ExcelType.XLSX)
                .Select(row => ((IDictionary<string, object?>)row).ToDictionary(kv => kv.Key, kv => kv.Value))
                .ToList();

            var batch = new DataBatch();
            foreach (var dict in rows)
            {
                var obj = new JsonObject();
                foreach (var kv in dict)
                {
                    obj[kv.Key] = ToJsonValue(kv.Value);
                }

                AddItem(batch, obj);
            }

            context.Logger?.LogInformation("spreadsheet Read Xlsx 解析 {Count} 行（HasHeader={HasHeader}）。", batch.Items.Count, hasHeader);
            return context.Ok(batch);
        }
        catch (InvalidDataException ex)
        {
            return context.ErrorResult("ParseError", $"Failed to parse Xlsx file: {ex.Message}");
        }
        catch (Exception ex)
        {
            // MiniExcel 对损坏文件可能抛出非 InvalidDataException 的异常。
            context.Logger?.LogWarning("spreadsheet 解析 Xlsx 失败：{Message}。", ex.Message);
            return context.ErrorResult("ParseError", $"Failed to parse Xlsx file: {ex.Message}");
        }
    }

    /// <summary>
    /// 内置 ODS 读取器（对应 <see cref="WriteOds"/>）。从 <c>content.xml</c> 解析 <c>table:table</c>，
    /// 支持按 <paramref name="sheetName"/> 选表（缺省取首个）；处理 <c>table:number-columns-repeated</c> 重复单元格；
    /// 按 <c>office:value-type</c> 映射为 JSON：string/boolean/date 为字符串，float/currency/percentage 为数字，空单元格为 JSON null。
    /// </summary>
    private static NodeExecutionResult ReadOds(NodeExecutionContext context, byte[] bytes, bool hasHeader, string? sheetName)
    {
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var contentEntry = zip.GetEntry("content.xml")
                ?? throw new InvalidDataException("ODS content.xml not found.");

            XDocument doc;
            using (var s = contentEntry.Open())
            {
                doc = XDocument.Load(s);
            }

            XNamespace table = OdsTableNs;
            XNamespace office = OdsOfficeNs;
            XNamespace text = OdsTextNs;

            var tables = doc.Descendants(table + "table").ToList();
            XElement? tableEl = null;
            if (!string.IsNullOrEmpty(sheetName))
            {
                tableEl = tables.FirstOrDefault(t => (string?)t.Attribute(table + "name") == sheetName);
            }

            tableEl ??= tables.FirstOrDefault();
            if (tableEl is null)
            {
                return context.ErrorResult("ParseError", "ODS contains no table.");
            }

            // 解析每行单元格为 JsonNode? 列表（含重复单元格展开）。
            var rawRows = new List<List<JsonNode?>>();
            foreach (var rowEl in tableEl.Elements(table + "table-row"))
            {
                var cells = new List<JsonNode?>();
                foreach (var cellEl in rowEl.Elements(table + "table-cell"))
                {
                    var repeatAttr = cellEl.Attribute(table + "number-columns-repeated");
                    var repeat = 1;
                    if (repeatAttr is not null && int.TryParse(repeatAttr.Value, out var r) && r > 0)
                    {
                        repeat = r;
                    }

                    var value = OdsCellValue(office, text, cellEl);
                    for (var k = 0; k < repeat; k++)
                    {
                        cells.Add(value);
                    }
                }

                rawRows.Add(cells);
            }

            var batch = new DataBatch();
            if (rawRows.Count == 0)
            {
                return context.Ok(batch);
            }

            if (hasHeader)
            {
                var headers = rawRows[0].Select((n, idx) => n is null ? $"col{idx}" : (n.GetValueKind() == System.Text.Json.JsonValueKind.String ? n.GetValue<string>() : n.ToString())).ToList();
                for (var r = 1; r < rawRows.Count; r++)
                {
                    var obj = new JsonObject();
                    var cells = rawRows[r];
                    for (var c = 0; c < headers.Count; c++)
                    {
                        var key = string.IsNullOrEmpty(headers[c]) ? $"col{c}" : headers[c];
                        obj[key] = c < cells.Count ? cells[c] : null;
                    }

                    AddItem(batch, obj);
                }
            }
            else
            {
                var colCount = rawRows.Max(row => row.Count);
                foreach (var cells in rawRows)
                {
                    var obj = new JsonObject();
                    for (var c = 0; c < colCount; c++)
                    {
                        obj[$"col{c}"] = c < cells.Count ? cells[c] : null;
                    }

                    AddItem(batch, obj);
                }
            }

            context.Logger?.LogInformation("spreadsheet Read Ods 解析 {Count} 行（HasHeader={HasHeader}）。", batch.Items.Count, hasHeader);
            return context.Ok(batch);
        }
        catch (InvalidDataException ex)
        {
            return context.ErrorResult("ParseError", $"Failed to parse Ods file: {ex.Message}");
        }
        catch (Exception ex)
        {
            context.Logger?.LogWarning("spreadsheet 解析 Ods 失败：{Message}。", ex.Message);
            return context.ErrorResult("ParseError", $"Failed to parse Ods file: {ex.Message}");
        }
    }

    private static JsonNode? OdsCellValue(XNamespace office, XNamespace text, XElement cellEl)
    {
        var type = (string?)cellEl.Attribute(office + "value-type");
        var paragraphs = cellEl.Elements(text + "p").Select(p => p.Value);
        var innerText = string.Concat(paragraphs);

        switch (type)
        {
            case "float":
            case "currency":
            case "percentage":
                var numAttr = (string?)cellEl.Attribute(office + "value") ?? innerText;
                if (decimal.TryParse(numAttr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var dec))
                {
                    return JsonValue.Create(dec);
                }

                return JsonValue.Create(innerText);
            case "boolean":
                var boolAttr = (string?)cellEl.Attribute(office + "boolean-value") ?? innerText;
                return JsonValue.Create(boolAttr == "true");
            case "date":
            case "time":
                return JsonValue.Create(innerText);
            case "string":
            case null:
            default:
                return string.IsNullOrEmpty(innerText) ? null : JsonValue.Create(innerText);
        }
    }

    // ODS / OpenDocument XML 命名空间常量。
    private const string OdsMimeType = "application/vnd.oasis.opendocument.spreadsheet";
    private static readonly XNamespace OdsOfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace OdsTableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace OdsTextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace OdsManifestNs = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    // ---- Write ----

    private async Task<NodeExecutionResult> WriteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
    {
        var inputBatch = context.GetInputBatch();
        if (inputBatch.Items.Count == 0)
        {
            return context.ErrorResult("MissingInput", "No input rows available to write.");
        }

        var rows = new List<Dictionary<string, object?>>();
        foreach (var item in inputBatch.Items)
        {
            if (item.Data is not JsonObject obj)
            {
                continue;
            }

            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in obj)
            {
                dict[kv.Key] = ToCellValue(kv.Value);
            }

            rows.Add(dict);
        }

        if (rows.Count == 0)
        {
            return context.ErrorResult("MissingInput", "No input rows available to write.");
        }

        byte[] bytes;
        try
        {
            bytes = Format switch
            {
                SpreadsheetFormat.Csv => WriteCsv(rows, HasHeader),
                SpreadsheetFormat.Xlsx => WriteWorkbook(rows, HasHeader, SheetName),
                SpreadsheetFormat.Ods => WriteOds(rows, HasHeader, SheetName),
                _ => throw new InvalidOperationException($"Unsupported format: {Format}")
            };
        }
        catch (InvalidDataException ex)
        {
            return context.ErrorResult("WriteError", ex.Message);
        }
        catch (Exception ex)
        {
            return context.ErrorResult("WriteError", $"Failed to write spreadsheet: {ex.Message}");
        }

        string? writtenPath = null;
        if (!string.IsNullOrEmpty(FilePath))
        {
            writtenPath = FilePath;
            try
            {
                await File.WriteAllBytesAsync(writtenPath, bytes, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return context.ErrorResult("WriteError", $"Failed to write file '{FilePath}': {ex.Message}");
            }
        }

        var result = new JsonObject
        {
            [OutputField] = JsonValue.Create(Convert.ToBase64String(bytes)),
            ["filePath"] = writtenPath is null ? null : JsonValue.Create(Path.GetFullPath(writtenPath)),
            ["fileName"] = writtenPath is null ? null : JsonValue.Create(Path.GetFileName(writtenPath)),
            ["rowCount"] = JsonValue.Create(rows.Count)
        };

        context.Logger?.LogInformation("spreadsheet Write {Format} 完成（行数：{Count}，字节：{Bytes}）。", Format, rows.Count, bytes.Length);
        return context.Ok(result);
    }

    private static byte[] WriteCsv(List<Dictionary<string, object?>> rows, bool hasHeader)
    {
        // 按首次出现顺序收集列名（各行列的并集）。
        var columns = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            foreach (var key in row.Keys)
            {
                if (seen.Add(key))
                {
                    columns.Add(key);
                }
            }
        }

        var sb = new StringBuilder();
        if (hasHeader && columns.Count > 0)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(CsvEncode(columns[i]));
            }

            sb.Append("\r\n");
        }

        foreach (var row in rows)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                if (i > 0) sb.Append(',');
                row.TryGetValue(columns[i], out var value);
                sb.Append(CsvEncode(value is null ? string.Empty : value?.ToString() ?? string.Empty));
            }

            sb.Append("\r\n");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] WriteWorkbook(List<Dictionary<string, object?>> rows, bool hasHeader, string? sheetName)
    {
        using var ms = new MemoryStream();
        MiniExcel.SaveAs(ms, rows, printHeader: hasHeader, sheetName: sheetName, excelType: ExcelType.XLSX);
        return ms.ToArray();
    }

    /// <summary>
    /// 内置 ODS 写入器（MiniExcel 1.45.0 不支持 ODS，故用 BCL <see cref="ZipArchive"/> + LINQ-to-XML 自实现）。
    /// 生成符合 OpenDocument 1.2 的最小表格包：<c>mimetype</c>（不压缩）、<c>META-INF/manifest.xml</c>、
    /// <c>content.xml</c>。字符串写为 <c>office:value-type="string"</c>；数字写为 <c>float</c>；
    /// 布尔/对象退化为字符串。列名取各行键首次出现顺序的并集。
    /// </summary>
    private static byte[] WriteOds(List<Dictionary<string, object?>> rows, bool hasHeader, string? sheetName)
    {
        var columns = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            foreach (var key in row.Keys)
            {
                if (seen.Add(key))
                {
                    columns.Add(key);
                }
            }
        }

        XNamespace office = OdsOfficeNs;
        XNamespace table = OdsTableNs;
        XNamespace text = OdsTextNs;
        var sheetNameStr = string.IsNullOrEmpty(sheetName) ? "Sheet1" : sheetName;

        var rowEls = new List<XElement>();

        // 表头行（hasHeader 时）。
        if (hasHeader && columns.Count > 0)
        {
            rowEls.Add(new XElement(table + "table-row",
                columns.Select(c => OdsCell(office, table, text, c))));
        }

        // 数据行。
        foreach (var row in rows)
        {
            rowEls.Add(new XElement(table + "table-row",
                columns.Select(c =>
                {
                    row.TryGetValue(c, out var value);
                    return OdsCell(office, table, text, value);
                })));
        }

        var content = new XElement(office + "document-content",
            new XAttribute(XNamespace.Xmlns + "office", office),
            new XAttribute(XNamespace.Xmlns + "table", table),
            new XAttribute(XNamespace.Xmlns + "text", text),
            new XAttribute(office + "version", "1.2"),
            new XElement(office + "body",
                new XElement(office + "spreadsheet",
                    new XElement(table + "table",
                        new XAttribute(table + "name", sheetNameStr),
                        rowEls))));

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), content);

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // 注意：ZipArchive 要求上一个条目的流关闭后才能创建新条目，故每条目用独立 using 块。
            var mimeEntry = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var mimeStream = mimeEntry.Open())
            {
                var mimeBytes = Encoding.ASCII.GetBytes(OdsMimeType);
                mimeStream.Write(mimeBytes, 0, mimeBytes.Length);
            }

            var manifestEntry = zip.CreateEntry("META-INF/manifest.xml");
            using (var manifestStream = manifestEntry.Open())
            {
                WriteOdsManifest(manifestStream);
            }

            var contentEntry = zip.CreateEntry("content.xml");
            using (var contentStream = contentEntry.Open())
            {
                doc.Save(contentStream);
            }
        }

        return ms.ToArray();
    }

    private static void WriteOdsManifest(Stream stream)
    {
        XNamespace manifest = OdsManifestNs;
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(manifest + "manifest",
                new XAttribute(XNamespace.Xmlns + "manifest", manifest),
                new XAttribute(manifest + "version", "1.2"),
                new XElement(manifest + "file-entry",
                    new XAttribute(manifest + "media-type", OdsMimeType),
                    new XAttribute(manifest + "full-path", "/")),
                new XElement(manifest + "file-entry",
                    new XAttribute(manifest + "media-type", "text/xml"),
                    new XAttribute(manifest + "full-path", "content.xml"))));
        doc.Save(stream);
    }

    private static XElement OdsCell(XNamespace office, XNamespace table, XNamespace text, object? value)
    {
        if (value is null)
        {
            return new XElement(table + "table-cell");
        }

        if (value is string s)
        {
            return new XElement(table + "table-cell",
                new XAttribute(office + "value-type", "string"),
                new XElement(text + "p", s));
        }

        if (value is bool b)
        {
            return new XElement(table + "table-cell",
                new XAttribute(office + "value-type", "boolean"),
                new XAttribute(office + "boolean-value", b ? "true" : "false"),
                new XElement(text + "p", b ? "true" : "false"));
        }

        // 数字：统一按 float 写出（long/int/decimal/double 等）。
        if (value is IConvertible)
        {
            var decimalValue = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return new XElement(table + "table-cell",
                new XAttribute(office + "value-type", "float"),
                new XAttribute(office + "value", decimalValue.ToString(CultureInfo.InvariantCulture)),
                new XElement(text + "p", decimalValue.ToString(CultureInfo.InvariantCulture)));
        }

        return new XElement(table + "table-cell",
            new XAttribute(office + "value-type", "string"),
            new XElement(text + "p", value.ToString() ?? string.Empty));
    }

    // ---- CSV 解析/编码 ----

    /// <summary>
    /// 内置 CSV 解析器（RFC 4180 子集）：处理引号包裹、<c>""</c> 转义、CRLF 行结束、
    /// 以及含逗号/换行的引用字段。字段在 EOF 处仍未闭合引号视为损坏，抛出 <see cref="InvalidDataException"/>。
    /// 返回每行字段列表；末尾换行不会额外产生空行。
    /// </summary>
    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var i = 0;
        var n = text.Length;

        while (i < n)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < n && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                    }
                    else
                    {
                        inQuotes = false;
                        i++;
                    }
                }
                else
                {
                    field.Append(c);
                    i++;
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                    i++;
                }
                else if (c == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                    i++;
                }
                else if (c == '\r' || c == '\n')
                {
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    if (c == '\r' && i + 1 < n && text[i + 1] == '\n')
                    {
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }
                }
                else
                {
                    field.Append(c);
                    i++;
                }
            }
        }

        if (inQuotes)
        {
            throw new InvalidDataException("CSV field is missing a closing quote.");
        }

        // 末尾无换行符的剩余字段/行补入（末尾换行已在上轮 flush 完成，这里不会重复）。
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// 内置 CSV 字段编码：含逗号/引号/CR/LF 或以空格开头/结尾时整体引号包裹，并对引号做 <c>""</c> 转义。
    /// </summary>
    private static string CsvEncode(string field)
    {
        if (field.Length == 0)
        {
            return string.Empty;
        }

        if (field.IndexOfAny([',', '"', '\r', '\n']) >= 0 || field[0] == ' ' || field[^1] == ' ')
        {
            var sb = new StringBuilder(field.Length + 2);
            sb.Append('"');
            foreach (var c in field)
            {
                if (c == '"')
                {
                    sb.Append("\"\"");
                }
                else
                {
                    sb.Append(c);
                }
            }

            sb.Append('"');
            return sb.ToString();
        }

        return field;
    }

    // ---- 值映射 ----

    /// <summary>
    /// 将 MiniExcel 读出的单元格值映射为 JSON 节点（保留数字/布尔/日期/字符串类型；null→JSON null）。
    /// </summary>
    private static JsonNode? ToJsonValue(object? value) => value switch
    {
        null => null,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        DateTime dt => JsonValue.Create(dt),
        DateTimeOffset dto => JsonValue.Create(dto),
        int or long or short or byte or sbyte or uint or ushort or ulong => JsonValue.Create(Convert.ToInt64(value)),
        decimal or double or float => JsonValue.Create(Convert.ToDecimal(value)),
        Guid g => JsonValue.Create(g.ToString()),
        _ => JsonValue.Create(value?.ToString())
    };

    /// <summary>
    /// 将上游 JSON 单元格值映射为可被 MiniExcel/CSV 写入的 CLR 值（null→null；数字/布尔/日期/字符串按类型）。
    /// 对象/数组序列化为 JSON 字符串。
    /// </summary>
    private static object? ToCellValue(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<bool>(out var b)) return b;
            if (jsonValue.TryGetValue<long>(out var l)) return l;
            if (jsonValue.TryGetValue<double>(out var d)) return d;
            if (jsonValue.TryGetValue<decimal>(out var dec)) return dec;
            if (jsonValue.TryGetValue<DateTime>(out var dt)) return dt;
            if (jsonValue.TryGetValue<string>(out var s)) return s;
            return jsonValue.ToString();
        }

        // 对象/数组：退化为 JSON 字符串。
        return node.ToJsonString();
    }

    private static void AddItem(DataBatch batch, JsonObject data)
    {
        batch.Items.Add(new DataItem
        {
            Data = data,
            Success = true,
            SourceIndex = batch.Items.Count
        });
    }
}

/// <summary>
/// spreadsheet 节点的读/写操作。
/// </summary>
public enum SpreadsheetOperation
{
    /// <summary>将表格读入 DataBatch，每行一个数据项。</summary>
    [Description("Read a spreadsheet into a DataBatch (one item per row).")]
    Read,

    /// <summary>将 DataBatch 的每行写为表格文件。</summary>
    [Description("Write a DataBatch's rows into a spreadsheet file.")]
    Write
}

/// <summary>
/// spreadsheet 节点支持的文件格式。
/// </summary>
public enum SpreadsheetFormat
{
    /// <summary>逗号分隔值，使用内置解析/写入器。</summary>
    [Description("Comma-separated values (built-in parser/writer).")]
    Csv,

    /// <summary>Excel 工作簿（XLSX），使用 MiniExcel。</summary>
    [Description("Excel workbook (XLSX), via MiniExcel.")]
    Xlsx,

    /// <summary>OpenDocument 表格（ODS），使用内置 BCL（ZipArchive + LINQ-to-XML）实现。</summary>
    [Description("OpenDocument Spreadsheet (ODS), via built-in BCL (ZipArchive + LINQ-to-XML).")]
    Ods
}
