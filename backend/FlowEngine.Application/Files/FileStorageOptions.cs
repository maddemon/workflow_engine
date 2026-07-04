namespace FlowEngine.Application.Files;

/// <summary>
/// 文件存储配置选项。
/// </summary>
public class FileStorageOptions
{
    /// <summary>
    /// 配置节名称。
    /// </summary>
    public const string SectionName = "FileStorage";

    /// <summary>
    /// 本地存储根目录。
    /// </summary>
    public string BasePath { get; set; } = "./storage/files";

    /// <summary>
    /// 单个文件大小上限（字节）。默认 50MB。设为 0 或负数表示不限制。
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>
    /// 允许的 MIME 类型白名单。空数组表示允许所有类型。
    /// </summary>
    public string[] AllowedContentTypes { get; set; } = [];
}
