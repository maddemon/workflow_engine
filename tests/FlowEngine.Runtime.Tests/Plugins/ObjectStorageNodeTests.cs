using System.Text;
using System.Text.Json.Nodes;
using Amazon.S3;
using Amazon.S3.Model;
using FlowEngine.Core;
using FlowEngine.Core.Entities;
using FlowEngine.Plugins.Storage;
using Moq;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// objectStorage 节点测试。所有操作测试通过内部 <see cref="ObjectStorageNode.ClientOverride"/> 注入
/// <see cref="Mock{IAmazonS3}"/>，覆盖 Upload（LocalPath + DataField base64）、Download（到文件 + 到输出 base64）、
/// Delete、List、MissingConnection、MissingKey、SDK 失败等路径。Connection 直接用 <see cref="CredentialValue"/> 构造。
/// </summary>
public sealed class ObjectStorageNodeTests
{
    [Fact]
    public async Task ExecuteAsync_Upload_LocalPath_CallsPutObject_WithFileBytes()
    {
        var content = Encoding.UTF8.GetBytes("local-file-content");
        var file = WriteTempFile(content);
        var captured = new MemoryStream();

        var mock = new Mock<IAmazonS3>();
        mock.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((req, _) =>
            {
                Assert.Equal("my-bucket", req.BucketName);
                Assert.Equal("docs/report.txt", req.Key);
                req.InputStream.CopyTo(captured);
            })
            .ReturnsAsync(new PutObjectResponse());

        var node = new ObjectStorageNode
        {
            ClientOverride = mock.Object,
            Connection = S3Credential(),
            Operation = ObjectStorageNode.ObjectStorageOperation.Upload,
            Key = "docs/report.txt",
            LocalPath = file
        };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("docs/report.txt", GetString(result.Output.Items[0].Data, "key"));
        Assert.Equal("my-bucket", GetString(result.Output.Items[0].Data, "bucket"));
        Assert.Equal(content.Length, GetLong(result.Output.Items[0].Data, "size"));
        Assert.True(GetBool(result.Output.Items[0].Data, "success"));
        mock.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(content, captured.ToArray());
    }

    [Fact]
    public async Task ExecuteAsync_Upload_DataFieldBase64_CallsPutObject_WithDecodedBytes()
    {
        var content = Encoding.UTF8.GetBytes("payload-from-field");
        var base64 = Convert.ToBase64String(content);
        var captured = new MemoryStream();

        var mock = new Mock<IAmazonS3>();
        mock.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((req, _) => req.InputStream.CopyTo(captured))
            .ReturnsAsync(new PutObjectResponse());

        var node = new ObjectStorageNode
        {
            ClientOverride = mock.Object,
            Connection = S3Credential(),
            Operation = ObjectStorageNode.ObjectStorageOperation.Upload,
            Key = "obj/key1",
            DataField = "fileContent"
        };

        var input = new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Success = true,
                    Data = new JsonObject { ["fileContent"] = base64 }
                }
            ]
        };

        var result = await node.ExecuteAsync(CreateContext(input), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("obj/key1", GetString(result.Output.Items[0].Data, "key"));
        Assert.Equal(content, captured.ToArray());
        mock.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Download_ToLocalPath_WritesFile()
    {
        var content = Encoding.UTF8.GetBytes("downloaded-bytes");
        var dest = Path.GetTempFileName();

        var mock = new Mock<IAmazonS3>();
        mock.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(content),
                ContentLength = content.Length,
                LastModified = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
            });

        var node = new ObjectStorageNode
        {
            ClientOverride = mock.Object,
            Connection = S3Credential(),
            Operation = ObjectStorageNode.ObjectStorageOperation.Download,
            Key = "obj/key2",
            LocalPath = dest
        };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("obj/key2", GetString(result.Output.Items[0].Data, "key"));
        Assert.Equal(content.Length, GetLong(result.Output.Items[0].Data, "size"));
        Assert.Equal(content, await File.ReadAllBytesAsync(dest));
        mock.Verify(x => x.GetObjectAsync(It.Is<GetObjectRequest>(r => r.BucketName == "my-bucket" && r.Key == "obj/key2"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Download_ToOutputBase64_ReturnsData()
    {
        var content = Encoding.UTF8.GetBytes("base64-download");
        var mock = new Mock<IAmazonS3>();
        mock.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(content),
                ContentLength = content.Length,
                LastModified = DateTime.UtcNow
            });

        var node = new ObjectStorageNode
        {
            ClientOverride = mock.Object,
            Connection = S3Credential(),
            Operation = ObjectStorageNode.ObjectStorageOperation.Download,
            Key = "obj/key3"
        };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        var data = GetString(result.Output.Items[0].Data, "data");
        Assert.Equal(content, Convert.FromBase64String(data!));
        Assert.Equal(content.Length, GetLong(result.Output.Items[0].Data, "size"));
    }

    [Fact]
    public async Task ExecuteAsync_Delete_CallsDeleteObject_ReturnsSuccess()
    {
        var mock = new Mock<IAmazonS3>();
        mock.Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());

        var node = new ObjectStorageNode
        {
            ClientOverride = mock.Object,
            Connection = S3Credential(),
            Operation = ObjectStorageNode.ObjectStorageOperation.Delete,
            Key = "obj/to-delete"
        };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("obj/to-delete", GetString(result.Output.Items[0].Data, "key"));
        Assert.True(GetBool(result.Output.Items[0].Data, "success"));
        mock.Verify(x => x.DeleteObjectAsync(It.Is<DeleteObjectRequest>(r => r.BucketName == "my-bucket" && r.Key == "obj/to-delete"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_List_ReturnsDataBatch_WithKeySizeLastModified()
    {
        var mock = new Mock<IAmazonS3>();
        mock.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects =
                [
                    new S3Object { Key = "a.txt", Size = 10, LastModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    new S3Object { Key = "b.txt", Size = 20, LastModified = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc) }
                ]
            });

        var node = new ObjectStorageNode
        {
            ClientOverride = mock.Object,
            Connection = S3Credential(),
            Operation = ObjectStorageNode.ObjectStorageOperation.List,
            Prefix = "a"
        };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(2, result.Output.Items.Count);
        Assert.Equal("a.txt", GetString(result.Output.Items[0].Data, "key"));
        Assert.Equal(10, GetLong(result.Output.Items[0].Data, "size"));
        Assert.Equal("b.txt", GetString(result.Output.Items[1].Data, "key"));
        Assert.Equal(20, GetLong(result.Output.Items[1].Data, "size"));
        mock.Verify(x => x.ListObjectsV2Async(It.Is<ListObjectsV2Request>(r => r.BucketName == "my-bucket" && r.Prefix == "a"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MissingConnection_ReturnsMissingConnection()
    {
        var node = new ObjectStorageNode
        {
            Operation = ObjectStorageNode.ObjectStorageOperation.Upload,
            Key = "x"
        };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(FlowConstants.ErrorCodes.MissingConnection, result.Error?.Code);
    }

    [Theory]
    [InlineData(ObjectStorageNode.ObjectStorageOperation.Upload)]
    [InlineData(ObjectStorageNode.ObjectStorageOperation.Download)]
    [InlineData(ObjectStorageNode.ObjectStorageOperation.Delete)]
    public async Task ExecuteAsync_MissingKey_ReturnsMissingKey(ObjectStorageNode.ObjectStorageOperation op)
    {
        var node = new ObjectStorageNode
        {
            ClientOverride = new Mock<IAmazonS3>().Object,
            Connection = S3Credential(),
            Operation = op
        };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("MissingKey", result.Error?.Code);
    }

    [Fact]
    public async Task ExecuteAsync_SdkFailure_ReturnsStorageError_NotThrown()
    {
        var source = WriteTempFile(Encoding.UTF8.GetBytes("boom"));
        var mock = new Mock<IAmazonS3>();
        mock.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("bucket unreachable"));

        var node = new ObjectStorageNode
        {
            ClientOverride = mock.Object,
            Connection = S3Credential(),
            Operation = ObjectStorageNode.ObjectStorageOperation.Upload,
            Key = "obj/key",
            LocalPath = source
        };

        var result = await node.ExecuteAsync(CreateContext(new DataBatch()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("StorageError", result.Error?.Code);
    }

    private static string WriteTempFile(byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), "flow-engine-objstore-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, content);
        return path;
    }

    private static bool GetBool(JsonNode? node, string key)
        => node?[key] is JsonValue value && value.TryGetValue<bool>(out var b) && b;

    private static string? GetString(JsonNode? node, string key)
        => node?[key] is JsonValue value ? value.GetValue<string>() : null;

    private static int GetInt(JsonNode? node, string key)
        => node?[key] is JsonValue value && value.TryGetValue<int>(out var i) ? i : 0;

    private static long GetLong(JsonNode? node, string key)
    {
        if (node?[key] is JsonValue value)
        {
            if (value.TryGetValue<long>(out var l)) return l;
            if (value.TryGetValue<int>(out var i)) return i;
        }

        return 0;
    }

    private static CredentialValue S3Credential()
    {
        return new CredentialValue
        {
            Name = "test-s3",
            Type = "s3",
            Fields = new Dictionary<string, string>
            {
                ["endpoint"] = "https://s3.example.com",
                ["accessKey"] = "AKIA-TEST",
                ["secretKey"] = "secret-key", // 测试占位，绝不输出到日志/异常
                ["bucket"] = "my-bucket",
                ["region"] = "us-east-1"
            }
        };
    }

    private static NodeExecutionContext CreateContext(DataBatch input)
    {
        return new NodeExecutionContext
        {
            Workflow = new Workflow { Id = Guid.NewGuid(), Name = "test" },
            ExecutionId = Guid.NewGuid(),
            Node = new NodeDefinition
            {
                Id = "objectStorage",
                TypeName = "objectStorage",
                Name = "objectStorage"
            },
            Inputs = new Dictionary<string, DataBatch>(StringComparer.OrdinalIgnoreCase)
            {
                [FlowConstants.PortNames.Input] = input
            }
        };
    }
}
