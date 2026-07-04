namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 文件存储抽象，提供文件的读写与管理。
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// 保存文件并返回唯一文件 ID。
    /// </summary>
    Task<string> SaveAsync(string fileName, Stream content, string projectId, CancellationToken ct = default);

    /// <summary>
    /// 读取文件流，不存在时返回 null。
    /// </summary>
    Task<Stream?> ReadAsync(string fileId, CancellationToken ct = default);

    /// <summary>
    /// 删除文件。
    /// </summary>
    Task<bool> DeleteAsync(string fileId, CancellationToken ct = default);

    /// <summary>
    /// 检查文件是否存在。
    /// </summary>
    Task<bool> ExistsAsync(string fileId, CancellationToken ct = default);
}
