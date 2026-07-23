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
/// writeFile 节点测试：覆盖 overwrite 往返、append 增长、目录自动创建、自定义 BinaryField，
/// 以及缺输入项/缺 BinaryField/空 FileName/非法 base64 等边界错误。
/// </summary>
public sealed class WriteFileNodeTests
{
    [Fact]
    public async Task ExecuteAsync_Overwrite_WritesDecodedBytesAndReturnsFilePath()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0xFE, 0xFF, 0xAB, 0xCD };
        var base64 = Convert.ToBase64String(bytes);
        var target = Path.Combine(Path.GetTempPath(), $"writefile_{Guid.NewGuid()}.bin");

        try
        {
            var node = new WriteFileNode
            {
                FileName = FileNameOf(target),
                WriteMode = FileWriteMode.Overwrite
            };

            var result = await node.ExecuteAsync(CreateContext(InputWith("data", base64)), CancellationToken.None);

            Assert.True(result.Success, result.Error?.Message);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(target, CancellationToken.None));
            Assert.Equal(Path.GetFullPath(target), GetString(result.Output.Items[0].Data, "filePath"));
            Assert.Equal(target, GetString(result.Output.Items[0].Data, "fileName"));
            Assert.Equal(bytes.Length, GetInt(result.Output.Items[0].Data, "bytesWritten"));
        }
        finally
        {
            File.Delete(target);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Append_GrowsFileToSumOfWrites()
    {
        var bytesA = new byte[] { 0x10, 0x20, 0x30 };
        var bytesB = new byte[] { 0xAA, 0xBB };
        var target = Path.Combine(Path.GetTempPath(), $"writefile_append_{Guid.NewGuid()}.bin");

        try
        {
            var node = new WriteFileNode { FileName = FileNameOf(target), WriteMode = FileWriteMode.Append };

            var first = await node.ExecuteAsync(CreateContext(InputWith("data", Convert.ToBase64String(bytesA))), CancellationToken.None);
            Assert.True(first.Success, first.Error?.Message);

            var second = await node.ExecuteAsync(CreateContext(InputWith("data", Convert.ToBase64String(bytesB))), CancellationToken.None);
            Assert.True(second.Success, second.Error?.Message);

            Assert.Equal(bytesA.Length + bytesB.Length, new FileInfo(target).Length);
            var written = await File.ReadAllBytesAsync(target, CancellationToken.None);
            Assert.Equal(bytesA, written[..bytesA.Length]);
            Assert.Equal(bytesB, written[bytesA.Length..]);
        }
        finally
        {
            File.Delete(target);
        }
    }

    [Fact]
    public async Task ExecuteAsync_PathGiven_CreatesMissingDirectory()
    {
        var bytes = new byte[] { 0x42 };
        var dir = Path.Combine(Path.GetTempPath(), $"writefile_dir_{Guid.NewGuid()}");
        var target = Path.Combine(dir, "out.bin");

        try
        {
            var node = new WriteFileNode
            {
                FileName = FileNameOf("out.bin"),
                Path = dir,
                WriteMode = FileWriteMode.Overwrite
            };

            var result = await node.ExecuteAsync(CreateContext(InputWith("data", Convert.ToBase64String(bytes))), CancellationToken.None);

            Assert.True(result.Success, result.Error?.Message);
            Assert.True(Directory.Exists(dir));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(target, CancellationToken.None));
            Assert.Equal(Path.GetFullPath(target), GetString(result.Output.Items[0].Data, "filePath"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_CustomBinaryField_UsesCustomFieldName()
    {
        var bytes = new byte[] { 0x7F, 0x80 };
        var target = Path.Combine(Path.GetTempPath(), $"writefile_custom_{Guid.NewGuid()}.bin");

        try
        {
            var node = new WriteFileNode
            {
                FileName = FileNameOf(target),
                BinaryField = "content",
                WriteMode = FileWriteMode.Overwrite
            };

            var result = await node.ExecuteAsync(
                CreateContext(InputWith("content", Convert.ToBase64String(bytes))),
                CancellationToken.None);

            Assert.True(result.Success, result.Error?.Message);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(target, CancellationToken.None));
        }
        finally
        {
            File.Delete(target);
        }
    }

    [Fact]
    public async Task ExecuteAsync_MissingInputItem_ReturnsMissingBinaryField()
    {
        var node = new WriteFileNode { FileName = FileNameOf("x.bin") };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingBinaryField", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_MissingBinaryField_ReturnsMissingBinaryField()
    {
        var node = new WriteFileNode { FileName = FileNameOf("x.bin") };

        // 输入项存在，但缺少默认 data 字段。
        var result = await node.ExecuteAsync(
            CreateContext(new DataBatch { Items = [new DataItem { Data = new JsonObject { ["other"] = "y" }, Success = true }] }),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingBinaryField", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyFileName_ReturnsMissingFileName()
    {
        var node = new WriteFileNode { FileName = FileNameOf("") };

        var result = await node.ExecuteAsync(CreateContext(InputWith("data", Convert.ToBase64String([1, 2, 3]))), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingFileName", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidBase64_ReturnsInvalidBase64()
    {
        var node = new WriteFileNode { FileName = FileNameOf("x.bin") };

        var result = await node.ExecuteAsync(
            CreateContext(InputWith("data", "!!!not-valid-base64!!!")),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("InvalidBase64", result.Error?.Code);
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

    private static int GetInt(JsonNode? node, string key)
        => node?[key] is JsonValue value ? value.GetValue<int>() : 0;

    private static Script FileNameOf(string fileName)
        => new Script
        {
            Source = $"'{fileName.Replace("\\", "\\\\").Replace("'", "\\'")}'",
            Language = ScriptLanguage.JavaScript,
            ReturnType = ScriptReturnType.String
        }.WithResolvedValue(JsonValue.Create(fileName));

    private static NodeExecutionContext CreateContext(DataBatch input)
    {
        return new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "test" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = "writeFile",
                TypeName = "writeFile",
                Name = "writeFile"
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
