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
        ValidateProjectId(projectId);

        var fileId = Guid.NewGuid().ToString("N");
        var projectDir = Path.Combine(_basePath, projectId);
        Directory.CreateDirectory(projectDir);

        var relativePath = $"{projectId}/{fileId}_{SanitizeFileName(fileName)}";
        var storagePath = Path.Combine(_basePath, relativePath);

        await using var fileStream = new FileStream(
            storagePath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);

        await content.CopyToAsync(fileStream, ct).ConfigureAwait(false);

        _logger?.LogDebug("文件已保存: {Path}", storagePath);

        return relativePath;
    }

    /// <inheritdoc/>
    public Task<Stream?> ReadAsync(string fileId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

        // 优先直接拼接路径（新格式：projectId/fileId_fileName）
        var filePath = TryResolveDirectPath(fileId);
        // 兼容旧数据（仅 fileId）：回退到全盘扫描
        filePath ??= FindFile(fileId);

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

        var filePath = TryResolveDirectPath(fileId) ?? FindFile(fileId);
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

        return Task.FromResult((TryResolveDirectPath(fileId) ?? FindFile(fileId)) is not null);
    }

    private static void ValidateProjectId(string projectId)
    {
        // projectId 直接作为目录名参与路径拼接，必须限定为合法 GUID，防止路径遍历（L4）。
        if (!Guid.TryParse(projectId, out _))
        {
            throw new ArgumentException("projectId 必须为合法 GUID。");
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        // 跨平台一致：使用硬编码集合，避免 Path.GetInvalidFileNameChars() 在 Linux/macOS 上遗漏 <>:" 等字符。
        const string invalidChars = "<>:\"/\\|?*\0";
        var sanitized = new char[fileName.Length];

        for (var i = 0; i < fileName.Length; i++)
        {
            sanitized[i] = invalidChars.Contains(fileName[i]) ? '_' : fileName[i];
        }

        return new string(sanitized);
    }

    /// <summary>
    /// 尝试直接拼接路径（新格式 fileId 含相对路径 projectId/fileId_fileName）。
    /// 返回 null 表示 fileId 为旧格式（纯 GUID），需要走 FindFile 回退。
    /// </summary>
    private string? TryResolveDirectPath(string fileId)
    {
        // 新格式包含目录分隔符：projectId/fileId_fileName
        if (!fileId.Contains('/') && !fileId.Contains('\\'))
        {
            return null;
        }

        // 防止路径遍历：确保拼接后仍在 _basePath 内
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, fileId));
        if (!fullPath.StartsWith(_basePath, StringComparison.Ordinal))
        {
            return null;
        }

        return File.Exists(fullPath) ? fullPath : null;
    }

    /// <summary>
    /// 全盘扫描查找文件（仅用于兼容旧格式 fileId）。
    /// </summary>
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
