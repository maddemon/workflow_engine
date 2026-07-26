using System.ComponentModel;
using System.Text.Json.Nodes;
using Amazon.S3;
using Amazon.S3.Model;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;

namespace FlowEngine.Plugins.Storage;

/// <summary>
/// S3 兼容对象存储节点。基于 <see cref="IAmazonS3"/> 实现 Upload / Download / Delete / List 四种操作，
/// 凭据使用内置 <c>s3</c> 类型（字段：endpoint/accessKey/secretKey/bucket/region）。
/// 密钥（accessKey/secretKey）为 secret，绝不输出到日志或异常消息。
/// </summary>
[NodeMeta(TypeName = "objectStorage", DisplayName = "Object Storage", Category = NodeCategory.Storage, Icon = "storage", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class ObjectStorageNode : NodeBase
{
    [Inject] public IExecutionLogger? Logger { get; private set; }
    /// <summary>
    /// S3 兼容存储操作类型。
    /// </summary>
    public enum ObjectStorageOperation
    {
        /// <summary>上传对象到存储桶。</summary>
        Upload,

        /// <summary>从存储桶下载对象。</summary>
        Download,

        /// <summary>从存储桶删除对象。</summary>
        Delete,

        /// <summary>列出存储桶内对象（可按前缀过滤）。</summary>
        List
    }

    /// <summary>
    /// S3 凭据（类型为 <c>s3</c>）。字段：endpoint/accessKey/secretKey/bucket/region。
    /// accessKey/secretKey 为 secret，绝不在日志或异常中输出。
    /// </summary>
    [Credential("s3")]
    [Description("S3-compatible credential (type: s3). Fields: endpoint/accessKey/secretKey/bucket/region.")]
    public CredentialValue? Connection { get; set; }

    /// <summary>
    /// 要执行的操作。默认 Upload。
    /// </summary>
    [Description("Operation to perform: Upload, Download, Delete, or List. Default Upload.")]
    public ObjectStorageOperation Operation { get; set; } = ObjectStorageOperation.Upload;

    /// <summary>
    /// 对象键（路径）。Upload/Download/Delete 必填。
    /// </summary>
    [Description("Object key/path. Required for Upload/Download/Delete.")]
    public string? Key { get; set; }

    /// <summary>
    /// 本地文件路径。Upload 时作为源文件；Download 时作为目标文件。为空时 Upload 取 DataField、Download 输出 base64。
    /// </summary>
    [Description("Local file path. Upload source or Download destination. When empty: Upload reads DataField, Download outputs base64.")]
    public string? LocalPath { get; set; }

    /// <summary>
    /// 输入 JSON 字段名，其 base64 内容为上传字节（当 <see cref="LocalPath"/> 为空且输入为二进制时）。
    /// </summary>
    [Description("Input JSON field name whose base64 content is the upload bytes when LocalPath is empty.")]
    public string? DataField { get; set; }

    /// <summary>
    /// 列表操作的可选键前缀过滤。
    /// </summary>
    [Description("Optional key prefix filter for List.")]
    public string? Prefix { get; set; }

    /// <summary>
    /// 测试可注入的 <see cref="IAmazonS3"/> 实例。非 null 时优先使用，便于以 mock 替换真实客户端。
    /// 运行时为 null，由 <see cref="CreateClient"/> 从凭据构造真实客户端。
    /// </summary>
    internal IAmazonS3? ClientOverride { get; set; }

    /// <inheritdoc />
    public override async Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        try
        {
            if (Connection is null)
            {
                throw new NodeExecutionException(FlowConstants.ErrorCodes.MissingConnection, "S3 connection credential is required.");
            }

            if (!Connection.Fields.TryGetValue("bucket", out var bucket) || string.IsNullOrWhiteSpace(bucket))
            {
                throw new NodeExecutionException("MissingBucket", "S3 bucket is required in the connection credential.");
            }

            var client = CreateClient();

            try
            {
                var result = Operation switch
                {
                    ObjectStorageOperation.Upload => await UploadAsync(client, bucket!, input, ct).ConfigureAwait(false),
                    ObjectStorageOperation.Download => await DownloadAsync(client, bucket!, ct).ConfigureAwait(false),
                    ObjectStorageOperation.Delete => await DeleteAsync(client, bucket!, ct).ConfigureAwait(false),
                    ObjectStorageOperation.List => await ListAsync(client, bucket!, ct).ConfigureAwait(false),
                    _ => throw new NodeExecutionException(FlowConstants.ErrorCodes.UnexpectedError, $"Unsupported operation: {Operation}.")
                };

                return result;
            }
            finally
            {
                // 仅当使用运行时自建的真实客户端时释放；注入的测试实例由调用方管理。
                if (ClientOverride is null && client is not null)
                {
                    client.Dispose();
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw new NodeExecutionException(FlowConstants.ErrorCodes.Cancelled, "objectStorage was cancelled.");
        }
        catch (AmazonS3Exception ex)
        {
            // 仅记录键信息，不记录凭据/密钥/桶级 secret。
            Logger?.LogError(ex, "objectStorage 操作失败（操作 {Operation}，键 {Key}）。", Operation, Key);
            throw new NodeExecutionException("StorageError", $"S3 operation failed: {ex.Message}");
        }
        catch (NodeExecutionException)
        {
            // 业务异常：保留原始错误码/消息，由 NodeBase 转换为失败结果。
            throw;
        }
        catch (Exception ex) when (ex is not NodeExecutionException)
        {
            Logger?.LogError(ex, "objectStorage 未预期错误（操作 {Operation}）。", Operation);
            throw new NodeExecutionException(FlowConstants.ErrorCodes.UnexpectedError, $"Unexpected error in objectStorage: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取 S3 客户端：优先使用注入的 <see cref="ClientOverride"/>，否则从凭据字段构造真实客户端。
    /// </summary>
    private IAmazonS3 CreateClient()
    {
        if (ClientOverride is not null)
        {
            return ClientOverride;
        }

        var fields = Connection!.Fields;
        var endpoint = fields.GetValueOrDefault("endpoint");
        var accessKey = fields.GetValueOrDefault("accessKey");
        var secretKey = fields.GetValueOrDefault("secretKey");
        var region = fields.GetValueOrDefault("region");

        var config = new AmazonS3Config();
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            config.ServiceURL = endpoint;
            // 兼容 MinIO 等 S3 兼容服务：使用路径风格寻址。
            config.ForcePathStyle = true;
        }

        if (!string.IsNullOrWhiteSpace(region))
        {
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region);
        }

        return new AmazonS3Client(accessKey, secretKey, config);
    }

    /// <summary>
    /// 上传：LocalPath 优先，否则从输入的 DataField 取 base64 字节。缺少来源 → MissingSource。
    /// </summary>
    private async Task<NodeHandlerOutput> UploadAsync(
        IAmazonS3 client, string bucket, NodeInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            throw new NodeExecutionException("MissingKey", "Key is required for Upload.");
        }

        byte[]? bytes = !string.IsNullOrWhiteSpace(LocalPath)
            ? await File.ReadAllBytesAsync(LocalPath, cancellationToken).ConfigureAwait(false)
            : ReadUploadBytesFromInput(input);
        if (bytes is null)
        {
            throw new NodeExecutionException("MissingSource", "Upload requires LocalPath or a DataField with base64 content.");
        }

        using var stream = new MemoryStream(bytes);
        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = Key,
            InputStream = stream,
            ContentType = "application/octet-stream"
        };

        await client.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);

        Logger?.LogInformation("objectStorage 上传完成：键 {Key}，大小 {Size} 字节。", Key, bytes.Length);

        return Single(new JsonObject
        {
            ["success"] = true,
            ["key"] = Key,
            ["bucket"] = bucket,
            ["size"] = bytes.Length
        });
    }

    /// <summary>
    /// 从输入批次首个 DataItem 的 <see cref="DataField"/> 读取 base64 字节；缺失或非法 → null。
    /// </summary>
    private byte[]? ReadUploadBytesFromInput(NodeInput input)
    {
        if (string.IsNullOrWhiteSpace(DataField))
        {
            return null;
        }

        var batch = input.InputBatch;
        if (batch.Items.Count == 0 || batch.Items[0].Data is not JsonObject obj)
        {
            return null;
        }

        if (obj[DataField] is not JsonValue value || !value.TryGetValue<string>(out var base64) || base64 is null)
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// 下载：写入 LocalPath（若设置），否则将字节以 base64 输出到结果字段 <c>data</c>。
    /// </summary>
    private async Task<NodeHandlerOutput> DownloadAsync(
        IAmazonS3 client, string bucket, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            throw new NodeExecutionException("MissingKey", "Key is required for Download.");
        }

        using var response = await client.GetObjectAsync(
            new GetObjectRequest { BucketName = bucket, Key = Key }, cancellationToken).ConfigureAwait(false);

        // 从实际流读取字节并计算大小，避免依赖响应头 ContentLength（部分客户端/模拟不填充）。
        using var mem = new MemoryStream();
        await response.ResponseStream.CopyToAsync(mem, cancellationToken).ConfigureAwait(false);
        var bytes = mem.ToArray();
        var size = bytes.Length;

        if (LocalPath is { Length: > 0 })
        {
            await File.WriteAllBytesAsync(LocalPath, bytes, cancellationToken).ConfigureAwait(false);
            Logger?.LogInformation("objectStorage 下载完成：键 {Key}，大小 {Size} 字节，写入 {LocalPath}。", Key, size, LocalPath);

            return Single(new JsonObject
            {
                ["success"] = true,
                ["key"] = Key,
                ["size"] = size
            });
        }

        var base64 = Convert.ToBase64String(bytes);
        Logger?.LogInformation("objectStorage 下载完成：键 {Key}，大小 {Size} 字节。", Key, size);

        return Single(new JsonObject
        {
            ["success"] = true,
            ["key"] = Key,
            ["size"] = size,
            ["data"] = base64
        });
    }

    /// <summary>
    /// 删除：删除 bucket/Key 对象，输出 { success, key }。
    /// </summary>
    private async Task<NodeHandlerOutput> DeleteAsync(
        IAmazonS3 client, string bucket, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            throw new NodeExecutionException("MissingKey", "Key is required for Delete.");
        }

        await client.DeleteObjectAsync(
            new DeleteObjectRequest { BucketName = bucket, Key = Key }, cancellationToken).ConfigureAwait(false);

        Logger?.LogInformation("objectStorage 删除完成：键 {Key}。", Key);

        return Single(new JsonObject
        {
            ["success"] = true,
            ["key"] = Key
        });
    }

    /// <summary>
    /// 列出：以 bucket（可选 Prefix）列出对象，将每个对象映射为一条 DataItem { key, size, lastModified }。
    /// </summary>
    private async Task<NodeHandlerOutput> ListAsync(
        IAmazonS3 client, string bucket, CancellationToken cancellationToken)
    {
        var request = new ListObjectsV2Request
        {
            BucketName = bucket,
            Prefix = Prefix
        };

        var response = await client.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);

        var items = new List<DataItem>(response.S3Objects.Count);
        for (var i = 0; i < response.S3Objects.Count; i++)
        {
            var s3 = response.S3Objects[i];
            items.Add(new DataItem
            {
                Success = true,
                SourceIndex = i,
                Data = new JsonObject
                {
                    ["key"] = s3.Key,
                    ["size"] = s3.Size,
                    ["lastModified"] = s3.LastModified?.ToString("o")
                }
            });
        }

        Logger?.LogInformation("objectStorage 列出完成：桶 {Bucket}，返回 {Count} 个对象。", bucket, items.Count);

        return NodeHandlerOutput.Data(new DataBatch { Items = items });
    }

    /// <summary>
    /// 将单条 JSON 对象包装为单条 DataItem 的输出。
    /// </summary>
    private static NodeHandlerOutput Single(JsonObject obj) =>
        NodeHandlerOutput.Data(new DataBatch
        {
            Items = [ new DataItem { Data = obj, Success = true, SourceIndex = 0 } ]
        });
}