using FlowEngine.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Infrastructure.Storage;

/// <summary>
/// 基于本地磁盘的文件存储实现。
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _basePath;
    private readonly ILogger<LocalFileStorage>? _logger;

    /// <summary>
    /// 初始化本地文件存储。
    /// </summary>
    public LocalFileStorage(string basePath = "./storage/files", ILogger<LocalFileStorage>? logger = null)
    {
        _basePath = Path.GetFullPath(basePath);
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> SaveAsync(string fileName, Stream content, string projectId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ValidatePath(projectId);

        var fileId = Guid.NewGuid().ToString("N");
        var projectDir = Path.Combine(_basePath, projectId);
        Directory.CreateDirectory(projectDir);

        var storagePath = Path.Combine(projectDir, $"{fileId}_{SanitizeFileName(fileName)}");

        await using var fileStream = new FileStream(
            storagePath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);

        await content.CopyToAsync(fileStream, ct).ConfigureAwait(false);

        _logger?.LogDebug("文件已保存: {Path}", storagePath);

        return fileId;
    }

    /// <inheritdoc/>
    public Task<Stream?> ReadAsync(string fileId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

        var filePath = FindFile(fileId);
        if (filePath is null)
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);

        return Task.FromResult<Stream?>(stream);
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(string fileId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

        var filePath = FindFile(fileId);
        if (filePath is null)
        {
            return Task.FromResult(false);
        }

        File.Delete(filePath);
        _logger?.LogDebug("文件已删除: {Path}", filePath);

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string fileId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

        return Task.FromResult(FindFile(fileId) is not null);
    }

    private static void ValidatePath(string path)
    {
        if (path.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("路径包含非法字符序列。");
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new char[fileName.Length];

        for (var i = 0; i < fileName.Length; i++)
        {
            sanitized[i] = Array.IndexOf(invalid, fileName[i]) >= 0 ? '_' : fileName[i];
        }

        return new string(sanitized);
    }

    private string? FindFile(string fileId)
    {
        if (!Directory.Exists(_basePath))
        {
            return null;
        }

        foreach (var dir in Directory.GetDirectories(_basePath))
        {
            foreach (var file in Directory.GetFiles(dir))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.StartsWith(fileId + "_", StringComparison.Ordinal))
                {
                    return file;
                }
            }
        }

        return null;
    }
}
