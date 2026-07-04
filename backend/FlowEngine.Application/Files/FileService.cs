using FlowEngine.Application.Dtos;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FlowEngine.Application.Files;

/// <summary>
/// 文件应用服务，编排文件上传、下载与元数据管理。
/// </summary>
public sealed class FileService(
    FlowEngineDbContext dbContext,
    IFileStorage fileStorage,
    IUserContext userContext,
    IOptions<FileStorageOptions> options)
{
    /// <summary>
    /// 上传文件并创建元数据记录。ProjectId 仅用于分类，不做隔离校验。
    /// </summary>
    /// <exception cref="InvalidOperationException">文件大小或类型未通过校验（GAP-07）。</exception>
    public async Task<UploadFileResult> UploadAsync(
        string fileName,
        Stream content,
        string? contentType,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        var opts = options.Value;

        // 文件大小校验（GAP-07）。
        if (opts.MaxFileSizeBytes > 0 && content.Length > opts.MaxFileSizeBytes)
        {
            throw new InvalidOperationException(
                $"文件大小 {content.Length} 字节超过上限 {opts.MaxFileSizeBytes} 字节。");
        }

        // 文件类型校验（GAP-07）：白名单非空时按 MIME 匹配，contentType 为空或不在白名单均拒绝（fail-closed，防绕过）。
        if (opts.AllowedContentTypes is { Length: > 0 } allowed)
        {
            if (string.IsNullOrWhiteSpace(contentType)
                || !allowed.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"文件类型 '{contentType ?? "<空>"}' 不在允许列表内。");
            }
        }

        var userId = userContext.UserId
            ?? throw new InvalidOperationException("用户未认证。");

        var storagePath = await fileStorage.SaveAsync(fileName, content, projectId.ToString(), cancellationToken)
            .ConfigureAwait(false);

        var storedFile = new StoredFile
        {
            FileName = fileName,
            ContentType = contentType,
            Size = content.Length,
            StoragePath = storagePath,
            ProjectId = projectId,
            UploadedBy = userId,
        };

        dbContext.StoredFiles.Add(storedFile);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new UploadFileResult
        {
            Id = storedFile.Id,
            FileName = storedFile.FileName,
            Size = storedFile.Size,
        };
    }

    /// <summary>
    /// 获取文件元数据。
    /// </summary>
    public async Task<StoredFileDto?> GetAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await dbContext.StoredFiles
            .FirstOrDefaultAsync(f => f.Id == fileId && !f.Deleted, cancellationToken)
            .ConfigureAwait(false);

        if (file is null)
        {
            return null;
        }

        return MapToDto(file);
    }

    /// <summary>
    /// 获取文件下载流。
    /// </summary>
    public async Task<Stream?> DownloadAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await dbContext.StoredFiles
            .FirstOrDefaultAsync(f => f.Id == fileId && !f.Deleted, cancellationToken)
            .ConfigureAwait(false);

        if (file is null)
        {
            return null;
        }

        return await fileStorage.ReadAsync(file.StoragePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 删除文件及元数据记录。
    /// </summary>
    public async Task<bool> DeleteAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await dbContext.StoredFiles
            .FirstOrDefaultAsync(f => f.Id == fileId && !f.Deleted, cancellationToken)
            .ConfigureAwait(false);

        if (file is null)
        {
            return false;
        }

        await fileStorage.DeleteAsync(file.StoragePath, cancellationToken).ConfigureAwait(false);

        file.Deleted = true;
        file.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// 获取项目下的所有文件。ProjectId 仅用于分类筛选，不做隔离。
    /// </summary>
    public async Task<IReadOnlyList<StoredFileDto>> GetAllByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var files = await dbContext.StoredFiles
            .Where(f => f.ProjectId == projectId && !f.Deleted)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return files.Select(MapToDto).ToList();
    }

    private static StoredFileDto MapToDto(StoredFile file)
    {
        return new StoredFileDto
        {
            Id = file.Id,
            FileName = file.FileName,
            ContentType = file.ContentType,
            Size = file.Size,
            ProjectId = file.ProjectId,
            UploadedBy = file.UploadedBy,
            CreatedAt = file.CreatedAt,
        };
    }
}
