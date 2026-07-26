using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Plugins.Standard;
using MiniExcelLibs;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// spreadsheet 节点测试：覆盖 CSV 读（含引号/逗号/换行的引用字段、HasHeader=false→col0/col1）、
/// CSV 写→读往返、XLSX/ODS 写→读往返（测试中用 MiniExcel 自校验）、缺输入→MissingInput、
/// 损坏 XLSX→ParseError/ReadError、SheetName/HasHeader 生效、自定义 InputField/OutputField，
/// 以及空输入 write→MissingInput、不支持 Format→InvalidFormat。
/// </summary>
public sealed class SpreadsheetNodeTests
{
    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), $"spreadsheet_tests_{Guid.NewGuid():N}");

    // ---- CSV read ----

    [Fact]
    public async Task Read_Csv_QuotedFieldWithCommaAndNewline_ParsesCorrectly()
    {
        // 引用字段内嵌逗号与换行，且用 "" 转义引号。
        const string csv = "\"col, one\",\"line1\nline2\",\"has \"\"quote\"\"\"";
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);

        var node = new SpreadsheetNode
        {
            Operation = SpreadsheetOperation.Read,
            Format = SpreadsheetFormat.Csv,
            HasHeader = false
        };

        var result = await ((INodeType)node).ExecuteAsync(
            CreateContext(InputWith("data", Convert.ToBase64String(bytes))),
            CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Single(result.Output.Items);
        var data = result.Output.Items[0].Data!;
        Assert.Equal("col, one", GetString(data, "col0"));
        Assert.Equal("line1\nline2", GetString(data, "col1"));
        Assert.Equal("has \"quote\"", GetString(data, "col2"));
    }

    [Fact]
    public async Task Read_Csv_HasHeaderFalse_UsesCol0Col1()
    {
        const string csv = "a,b\nc,d";
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);

        var node = new SpreadsheetNode
        {
            Operation = SpreadsheetOperation.Read,
            Format = SpreadsheetFormat.Csv,
            HasHeader = false
        };

        var result = await ((INodeType)node).ExecuteAsync(
            CreateContext(InputWith("data", Convert.ToBase64String(bytes))),
            CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
        Assert.Equal("a", GetString(result.Output.Items[0].Data, "col0"));
        Assert.Equal("b", GetString(result.Output.Items[0].Data, "col1"));
        Assert.Equal("c", GetString(result.Output.Items[1].Data, "col0"));
        Assert.Equal("d", GetString(result.Output.Items[1].Data, "col1"));
    }

    [Fact]
    public async Task Read_Csv_HasHeaderTrue_MapsNamedColumns()
    {
        const string csv = "name,age\nAlice,30\nBob,25";
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);

        var node = new SpreadsheetNode
        {
            Operation = SpreadsheetOperation.Read,
            Format = SpreadsheetFormat.Csv,
            HasHeader = true
        };

        var result = await ((INodeType)node).ExecuteAsync(
            CreateContext(InputWith("data", Convert.ToBase64String(bytes))),
            CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
        Assert.Equal("Alice", GetString(result.Output.Items[0].Data, "name"));
        Assert.Equal("30", GetString(result.Output.Items[0].Data, "age"));
        Assert.Equal("Bob", GetString(result.Output.Items[1].Data, "name"));
    }

    // ---- CSV write -> read round trip ----

    [Fact]
    public async Task WriteThenRead_Csv_RoundTrip_PreservesRows()
    {
        var rows = new DataBatch
        {
            Items =
            [
                Row("name", "Alice", "city", "Beijing"),
                Row("name", "Bob", "city", "Shanghai"),
                Row("name", "Cara", "city", "Guangzhou")
            ]
        };

        var csvFile = Path.Combine(TempDir, "out.csv");
        var writeNode = new SpreadsheetNode
        {
            Operation = SpreadsheetOperation.Write,
            Format = SpreadsheetFormat.Csv,
            HasHeader = true,
            FilePath = csvFile
        };
        var writeResult = await ((INodeType)writeNode).ExecuteAsync(CreateContext(rows), CancellationToken.None);
        Assert.True(writeResult.Success, writeResult.Error?.Message);
        Assert.Equal(3, GetInt(writeResult.Output.Items[0].Data, "rowCount"));

        var readNode = new SpreadsheetNode
        {
            Operation = SpreadsheetOperation.Read,
            Format = SpreadsheetFormat.Csv,
            HasHeader = true,
            FilePath = csvFile
        };
        var readResult = await ((INodeType)readNode).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);
        Assert.True(readResult.Success, readResult.Error?.Message);
        Assert.Equal(3, readResult.Output.Items.Count);
        Assert.Equal("Alice", GetString(readResult.Output.Items[0].Data, "name"));
        Assert.Equal("Beijing", GetString(readResult.Output.Items[0].Data, "city"));
        Assert.Equal("Cara", GetString(readResult.Output.Items[2].Data, "name"));
    }

    // ---- XLSX write -> read round trip (MiniExcel self-verify) ----

    [Fact]
    public async Task WriteThenRead_Xlsx_RoundTrip_PreservesRows()
    {
        var rows = SampleRows();

        var xlsxFile = Path.Combine(TempDir, "out.xlsx");
        var writeNode = new SpreadsheetNode
        {
            Operation = SpreadsheetOperation.Write,
            Format = SpreadsheetFormat.Xlsx,
            HasHeader = true,
            SheetName = "Data",
            FilePath = xlsxFile
        };
        var writeResult = await ((INodeType)writeNode).ExecuteAsync(CreateContext(rows), CancellationToken.None);
        Assert.True(writeResult.Success, writeResult.Error?.Message);
        Assert.Equal(2, GetInt(writeResult.Output.Items[0].Data, "rowCount"));

        // 自校验：直接用 MiniExcel 读回文件，确认类型/列一致。
        List<Dictionary<string, object?>> raw;
        using (var fs = File.OpenRead(xlsxFile))
        {
            raw = MiniExcel.Query(fs, useHeaderRow: true, sheetName: "Data", excelType: ExcelType.XLSX)
                .Select(d => ((IDictionary<string, object?>)d).ToDictionary(kv => kv.Key, kv => kv.Value))
                .ToList();
        }

        Assert.Equal(2, raw.Count);
        Assert.Contains("name", raw[0].Keys);
        Assert.Equal("Alice", raw[0]["name"]?.ToString());

        var readNode = new SpreadsheetNode
        {
            Operation = SpreadsheetOperation.Read,
            Format = SpreadsheetFormat.Xlsx,
            HasHeader = true,
            SheetName = "Data",
            FilePath = xlsxFile
        };
        var readResult = await ((INodeType)readNode).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);
        Assert.True(readResult.Success, readResult.Error?.Message);
        Assert.Equal(2, readResult.Output.Items.Count);
        Assert.Equal("Alice", GetString(readResult.Output.Items[0].Data, "name"));
        Assert.Equal("30", Norm(readResult.Output.Items[0].Data!["age"]));
        Assert.Equal("Bob", GetString(readResult.Output.Items[1].Data, "name"));
    }

    // ---- ODS write -> read round trip (MiniExcel self-verify) ----

    [Fact]
    public async Task WriteThenRead_Ods_RoundTrip_PreservesRows()
    {
        var rows = SampleRows();

        var odsFile = Path.Combine(TempDir, "out.ods");
        var writeNode = new SpreadsheetNode
        {
            Operation = SpreadsheetOperation.Write,
            Format = SpreadsheetFormat.Ods,
            HasHeader = true,
            SheetName = "Data",
            FilePath = odsFile
        };
        var writeResult = await ((INodeType)writeNode).ExecuteAsync(CreateContext(rows), CancellationToken.None);
        Assert.True(writeResult.Success, writeResult.Error?.Message);
        Assert.Equal(2, GetInt(writeResult.Output.Items[0].Data, "rowCount"));

        // MiniExcel 1.45.0 不支持 ODS，故用节点自身（内置 ODS 读取器）做往返自校验。
        var readNode = new SpreadsheetNode
        {
            Operation = SpreadsheetOperation.Read,
            Format = SpreadsheetFormat.Ods,
            HasHeader = true,
            SheetName = "Data",
            FilePath = odsFile
        };
        var readResult = await ((INodeType)readNode).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);
        Assert.True(readResult.Success, readResult.Error?.Message);
        Assert.Equal(2, readResult.Output.Items.Count);
        Assert.Equal("Alice", GetString(readResult.Output.Items[0].Data, "name"));
        Assert.Equal("30", Norm(readResult.Output.Items[0].Data!["age"]));
        Assert.Equal("Bob", GetString(readResult.Output.Items[1].Data, "name"));
        Assert.Equal("25", Norm(readResult.Output.Items[1].Data!["age"]));
    }

    // ---- Missing input ----

    [Fact]
    public async Task Read_NoFilePathAndMissingField_ReturnsMissingInput()
    {
        var node = new SpreadsheetNode
        {
            Operation = SpreadsheetOperation.Read,
            Format = SpreadsheetFormat.Csv
        };

        // 输入项存在，但缺默认 data 字段。
        var input = new DataBatch { Items = [new DataItem { Data = new JsonObject { ["other"] = "x" }, Success = true }] };
        var result = await ((INodeType)node).ExecuteAsync(CreateContext(input), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingInput", result.Error?.Code);
    }

    [Fact]
    public async Task Write_EmptyInput_ReturnsMissingInput()
    {
        var node = new SpreadsheetNode
        {
            Operation = SpreadsheetOperation.Write,
            Format = SpreadsheetFormat.Csv
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingInput", result.Error?.Code);
    }

    // ---- Corrupt spreadsheet ----

    [Fact]
    public async Task Read_CorruptXlsx_ReturnsParseErrorOrReadError()
    {
        var file = Path.Combine(TempDir, "corrupt.xlsx");
        await File.WriteAllTextAsync(file, "this is not a spreadsheet file at all");

        var node = new SpreadsheetNode
        {
            Operation = SpreadsheetOperation.Read,
            Format = SpreadsheetFormat.Xlsx,
            FilePath = file
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(result.Error?.Code, new[] { "ParseError", "ReadError" });
    }

    // ---- SheetName / HasHeader honored ----

    [Fact]
    public async Task Read_Xlsx_HasHeaderFalse_Honored()
    {
        var rows = SampleRows();
        var xlsxFile = Path.Combine(TempDir, "nohdr.xlsx");

        var writeNode = new SpreadsheetNode
        {
            Operation = SpreadsheetOperation.Write,
            Format = SpreadsheetFormat.Xlsx,
            HasHeader = false,
            SheetName = "S",
            FilePath = xlsxFile
        };
        var writeResult = await ((INodeType)writeNode).ExecuteAsync(CreateContext(rows), CancellationToken.None);
        Assert.True(writeResult.Success, writeResult.Error?.Message);

        var readNode = new SpreadsheetNode
        {
            Operation = SpreadsheetOperation.Read,
            Format = SpreadsheetFormat.Xlsx,
            HasHeader = false,
            SheetName = "S",
            FilePath = xlsxFile
        };
        var readResult = await ((INodeType)readNode).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);
        Assert.True(readResult.Success, readResult.Error?.Message);
        // HasHeader=false：写入时无表头，读回按列字母 A/B/C，且包含全部数据行。
        Assert.Equal(2, readResult.Output.Items.Count);
        Assert.NotNull(readResult.Output.Items[0].Data!["A"]);
        Assert.NotNull(readResult.Output.Items[1].Data!["C"]);
    }

    [Fact]
    public async Task Read_Xlsx_SheetNameHonored_SelectsNamedSheet()
    {
        var rows = SampleRows();
        var xlsxFile = Path.Combine(TempDir, "sheets.xlsx");

        var writeNode = new SpreadsheetNode
        {
            Operation = SpreadsheetOperation.Write,
            Format = SpreadsheetFormat.Xlsx,
            HasHeader = true,
            SheetName = "Target",
            FilePath = xlsxFile
        };
        var writeResult = await ((INodeType)writeNode).ExecuteAsync(CreateContext(rows), CancellationToken.None);
        Assert.True(writeResult.Success, writeResult.Error?.Message);

        var readNode = new SpreadsheetNode
        {
            Operation = SpreadsheetOperation.Read,
            Format = SpreadsheetFormat.Xlsx,
            HasHeader = true,
            SheetName = "Target",
            FilePath = xlsxFile
        };
        var readResult = await ((INodeType)readNode).ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);
        Assert.True(readResult.Success, readResult.Error?.Message);
        Assert.Equal("Alice", GetString(readResult.Output.Items[0].Data, "name"));
    }

    // ---- Custom InputField / OutputField ----

    [Fact]
    public async Task Write_CustomOutputField_EmitsBase64InCustomField()
    {
        var rows = new DataBatch { Items = [Row("name", "Alice", "city", "Beijing")] };
        var csvFile = Path.Combine(TempDir, "custom_out.csv");

        var node = new SpreadsheetNode
        {
            Operation = SpreadsheetOperation.Write,
            Format = SpreadsheetFormat.Csv,
            HasHeader = true,
            FilePath = csvFile,
            OutputField = "content"
        };
        var result = await ((INodeType)node).ExecuteAsync(CreateContext(rows), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        // 自定义 OutputField="content" 应承载 base64；默认 "data" 不应出现。
        Assert.NotNull(GetString(result.Output.Items[0].Data, "content"));
        Assert.Null(GetString(result.Output.Items[0].Data, "data"));
        Assert.Equal(1, GetInt(result.Output.Items[0].Data, "rowCount"));
    }

    [Fact]
    public async Task Read_CustomInputField_ReadsFromCustomField()
    {
        const string csv = "name\nZoe";
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);

        var node = new SpreadsheetNode
        {
            Operation = SpreadsheetOperation.Read,
            Format = SpreadsheetFormat.Csv,
            HasHeader = true,
            InputField = "payload"
        };
        var input = InputWith("payload", Convert.ToBase64String(bytes));
        var result = await ((INodeType)node).ExecuteAsync(CreateContext(input), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("Zoe", GetString(result.Output.Items[0].Data, "name"));
    }

    // ---- Invalid format ----

    [Fact]
    public async Task Read_InvalidFormat_ReturnsInvalidFormat()
    {
        var node = new SpreadsheetNode
        {
            Operation = SpreadsheetOperation.Read,
            Format = (SpreadsheetFormat)999
        };

        var result = await ((INodeType)node).ExecuteAsync(CreateContext(InputWith("data", "AQID")), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("InvalidFormat", result.Error?.Code);
    }

    // ---- helpers ----

    private static DataBatch SampleRows()
        => new DataBatch
        {
            Items =
            [
                Row("name", "Alice", "age", 30, "city", "Beijing"),
                Row("name", "Bob", "age", 25, "city", "Shanghai")
            ]
        };

    private static DataItem Row(params object[] pairs)
    {
        var obj = new JsonObject();
        for (var i = 0; i + 1 < pairs.Length; i += 2)
        {
            var key = (string)pairs[i];
            var value = pairs[i + 1];
            obj[key] = value switch
            {
                string s => JsonValue.Create(s),
                int n => JsonValue.Create(n),
                long n => JsonValue.Create(n),
                bool b => JsonValue.Create(b),
                _ => JsonValue.Create(value.ToString())
            };
        }

        return new DataItem { Data = obj, Success = true };
    }

    private static string Norm(JsonNode? n) => n switch
    {
        null => "",
        JsonValue jv when jv.TryGetValue<long>(out var l) => l.ToString(CultureInfo.InvariantCulture),
        JsonValue jv when jv.TryGetValue<double>(out var d) => d.ToString(CultureInfo.InvariantCulture),
        JsonValue jv when jv.TryGetValue<decimal>(out var dec) => dec.ToString(CultureInfo.InvariantCulture),
        JsonValue jv when jv.TryGetValue<bool>(out var b) => b.ToString(),
        JsonValue jv when jv.TryGetValue<string>(out var s) => s,
        _ => n.ToString()
    };

    private static DataBatch InputWith(string field, string base64)
        => new DataBatch { Items = [new DataItem { Data = new JsonObject { [field] = base64 }, Success = true }] };

    private static string? GetString(JsonNode? node, string key)
        => node?[key] is JsonValue value ? value.GetValue<string>() : null;

    private static int GetInt(JsonNode? node, string key)
        => node?[key] is JsonValue value ? value.GetValue<int>() : 0;

    private static NodeExecutionContext CreateContext(DataBatch input)
    {
        return new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "test" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition { Id = "spreadsheet", TypeName = "spreadsheet", Name = "spreadsheet" },
            Inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
            {
                [FlowConstants.PortNames.Input] = input
            },
            CancellationToken = CancellationToken.None
        };
    }

    public SpreadsheetNodeTests()
    {
        Directory.CreateDirectory(TempDir);
    }
}
