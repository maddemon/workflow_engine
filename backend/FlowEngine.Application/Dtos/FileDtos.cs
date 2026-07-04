namespace FlowEngine.Application.Dtos;

/// <summary>
/// 存储文件响应 DTO。
/// </summary>
public sealed record StoredFileDto
{
    /// <summary>
    /// 文件 ID。
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// 原始文件名。
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// MIME 类型。
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// 文件大小（字节）。
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// 所属项目 ID。
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// 上传者用户 ID。
    /// </summary>
    public Guid UploadedBy { get; init; }

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// 文件上传结果 DTO。
/// </summary>
public sealed record UploadFileResult
{
    /// <summary>
    /// 文件 ID。
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// 原始文件名。
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// 文件大小（字节）。
    /// </summary>
    public long Size { get; init; }
}
