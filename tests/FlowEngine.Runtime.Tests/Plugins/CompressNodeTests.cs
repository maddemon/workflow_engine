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
/// compress 节点测试：覆盖 Zip/Gzip/Tar 三向压缩 + 对应解压往返（字节一致）、
/// 损坏归档（zip/gz/tar）→ CorruptArchive、缺输入/非 base64 → MissingInput、
/// 自定义 InputField/OutputField 生效，以及 tar 条目名超长 → EntryNameTooLong。
/// </summary>
public sealed class CompressNodeTests
{
    private static readonly byte[] SampleBytes =
        Encoding.UTF8.GetBytes("flow-engine compress node round-trip payload \u4e2d\u6587\u00ff\u00fe");

    // ---- Zip -> Unzip ----

    [Fact]
    public async Task ZipThenUnzip_RoundTrip_BytesMatch()
    {
        var zipNode = new CompressNode { Operation = CompressOperation.Zip };
        var zipResult = await zipNode.ExecuteAsync(
            CreateContext(InputWith("data", Convert.ToBase64String(SampleBytes))),
            CancellationToken.None);
        Assert.True(zipResult.Success, zipResult.Error?.Message);
        Assert.Equal("content.zip", GetString(zipResult.Output.Items[0].Data, "fileName"));

        var unzipNode = new CompressNode { Operation = CompressOperation.Unzip };
        var unzipResult = await unzipNode.ExecuteAsync(
            CreateContext(InputWith("data", GetString(zipResult.Output.Items[0].Data, "data")!)),
            CancellationToken.None);

        Assert.True(unzipResult.Success, unzipResult.Error?.Message);
        Assert.Single(unzipResult.Output.Items);
        Assert.Equal(SampleBytes, Convert.FromBase64String(GetString(unzipResult.Output.Items[0].Data, "data")!));
        Assert.Equal("content", GetString(unzipResult.Output.Items[0].Data, "name"));
    }

    // ---- Gzip -> Gunzip ----

    [Fact]
    public async Task GzipThenGunzip_RoundTrip_BytesMatch()
    {
        var gzipNode = new CompressNode { Operation = CompressOperation.Gzip };
        var gzipResult = await gzipNode.ExecuteAsync(
            CreateContext(InputWith("data", Convert.ToBase64String(SampleBytes))),
            CancellationToken.None);
        Assert.True(gzipResult.Success, gzipResult.Error?.Message);

        var gunzipNode = new CompressNode { Operation = CompressOperation.Gunzip };
        var gunzipResult = await gunzipNode.ExecuteAsync(
            CreateContext(InputWith("data", GetString(gzipResult.Output.Items[0].Data, "data")!)),
            CancellationToken.None);

        Assert.True(gunzipResult.Success, gunzipResult.Error?.Message);
        Assert.Single(gunzipResult.Output.Items);
        Assert.Equal(SampleBytes, Convert.FromBase64String(GetString(gunzipResult.Output.Items[0].Data, "data")!));
    }

    // ---- Tar -> Untar ----

    [Fact]
    public async Task TarThenUntar_RoundTrip_BytesMatch()
    {
        var tarNode = new CompressNode { Operation = CompressOperation.Tar };
        var tarResult = await tarNode.ExecuteAsync(
            CreateContext(InputWith("data", Convert.ToBase64String(SampleBytes))),
            CancellationToken.None);
        Assert.True(tarResult.Success, tarResult.Error?.Message);
        Assert.Equal("content.tar", GetString(tarResult.Output.Items[0].Data, "fileName"));

        var untarNode = new CompressNode { Operation = CompressOperation.Untar };
        var untarResult = await untarNode.ExecuteAsync(
            CreateContext(InputWith("data", GetString(tarResult.Output.Items[0].Data, "data")!)),
            CancellationToken.None);

        Assert.True(untarResult.Success, untarResult.Error?.Message);
        Assert.Single(untarResult.Output.Items);
        Assert.Equal(SampleBytes, Convert.FromBase64String(GetString(untarResult.Output.Items[0].Data, "data")!));
        Assert.Equal("content", GetString(untarResult.Output.Items[0].Data, "name"));
    }

    // ---- Corrupt archives ----

    [Fact]
    public async Task Unzip_CorruptArchive_ReturnsCorruptArchive()
    {
        var node = new CompressNode { Operation = CompressOperation.Unzip };
        var result = await node.ExecuteAsync(
            CreateContext(InputWith("data", Convert.ToBase64String(Encoding.UTF8.GetBytes("this is not a zip archive")))),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("CorruptArchive", result.Error?.Code);
    }

    [Fact]
    public async Task Gunzip_CorruptArchive_ReturnsCorruptArchive()
    {
        var node = new CompressNode { Operation = CompressOperation.Gunzip };
        var result = await node.ExecuteAsync(
            CreateContext(InputWith("data", Convert.ToBase64String(Encoding.UTF8.GetBytes("this is not gz data")))),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("CorruptArchive", result.Error?.Code);
    }

    [Fact]
    public async Task Untar_CorruptArchive_ReturnsCorruptArchive()
    {
        var node = new CompressNode { Operation = CompressOperation.Untar };
        var result = await node.ExecuteAsync(
            CreateContext(InputWith("data", Convert.ToBase64String(Encoding.UTF8.GetBytes("this is not a tar archive")))),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("CorruptArchive", result.Error?.Code);
    }

    // ---- Missing input / non-base64 ----

    [Fact]
    public async Task MissingInputItem_ReturnsMissingInput()
    {
        var node = new CompressNode { Operation = CompressOperation.Zip };
        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingInput", result.Error?.Code);
    }

    [Fact]
    public async Task NonBase64Input_ReturnsMissingInput()
    {
        var node = new CompressNode { Operation = CompressOperation.Zip };
        var result = await node.ExecuteAsync(
            CreateContext(InputWith("data", "!!!not-valid-base64!!!")),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingInput", result.Error?.Code);
    }

    // ---- Custom InputField / OutputField ----

    [Fact]
    public async Task CustomInputAndOutputFields_RoundTripApplied()
    {
        const string inField = "payload";
        const string outField = "archive";

        var zipNode = new CompressNode
        {
            Operation = CompressOperation.Zip,
            InputField = inField,
            OutputField = outField
        };
        var zipResult = await zipNode.ExecuteAsync(
            CreateContext(InputWith(inField, Convert.ToBase64String(SampleBytes))),
            CancellationToken.None);
        Assert.True(zipResult.Success, zipResult.Error?.Message);
        Assert.NotNull(GetString(zipResult.Output.Items[0].Data, outField));

        var unzipNode = new CompressNode
        {
            Operation = CompressOperation.Unzip,
            InputField = outField,
            OutputField = outField
        };
        var unzipResult = await unzipNode.ExecuteAsync(
            CreateContext(InputWith(outField, GetString(zipResult.Output.Items[0].Data, outField)!)),
            CancellationToken.None);

        Assert.True(unzipResult.Success, unzipResult.Error?.Message);
        Assert.Equal(SampleBytes, Convert.FromBase64String(GetString(unzipResult.Output.Items[0].Data, outField)!));
    }

    // ---- Tar entry name too long ----

    [Fact]
    public async Task Tar_EntryNameTooLong_ReturnsEntryNameTooLong()
    {
        var node = new CompressNode
        {
            Operation = CompressOperation.Tar,
            EntryName = new string('a', 101)
        };
        var result = await node.ExecuteAsync(
            CreateContext(InputWith("data", Convert.ToBase64String(SampleBytes))),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("EntryNameTooLong", result.Error?.Code);
    }

    private static DataBatch InputWith(string field, string base64)
        => new DataBatch
        {
            Items =
            [
                new DataItem { Data = new JsonObject { [field] = base64 }, Success = true }
            ]
        };

    private static string? GetString(JsonNode? node, string key)
        => node?[key] is JsonValue value ? value.GetValue<string>() : null;

    private static NodeExecutionContext CreateContext(DataBatch input)
    {
        return new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "test" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = "compress",
                TypeName = "compress",
                Name = "compress"
            },
            Inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
            {
                [FlowConstants.PortNames.Input] = input
            },
            ScriptCache = new ScriptCache(Options.Create(new JsEngineOptions())),
            EngineOptions = new JsEngineOptions(),
            CancellationToken = CancellationToken.None
        };
    }
}
