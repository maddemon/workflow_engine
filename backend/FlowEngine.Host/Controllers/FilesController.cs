using FlowEngine.Application.Audit;
using FlowEngine.Application.Dtos;
using FlowEngine.Application.Files;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;
using FlowEngine.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace FlowEngine.Host.Controllers;

/// <summary>
/// 文件管理 API。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/files")]
public class FilesController(
    FileService fileService,
    IEventBus eventBus,
    AuditEventFactory auditFactory,
    IStringLocalizer<SharedResource> localizer) : ControllerBase
{
    /// <summary>
    /// 上传文件。
    /// </summary>
    [HttpPost("upload")]
    [AuthorizePermission(Scope.File, Operation.Write)]
    [RequestSizeLimit(104_857_600)] // 100 MB
    public async Task<ActionResult<UploadFileResult>> Upload(
        IFormFile file,
        [FromQuery] Guid projectId,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return this.BadRequestError("FileRequired", localizer["FileRequired"]);
        }

        await using var stream = file.OpenReadStream();
        try
        {
            var result = await fileService.UploadAsync(
                file.FileName,
                stream,
                file.ContentType,
                projectId,
                cancellationToken).ConfigureAwait(false);

            return Ok(result);
        }
        catch (PermissionDeniedException ex)
        {
            await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
                AuditEventTypes.FileAccessDenied,
                "File",
                Guid.Empty,
                new Dictionary<string, object>
                {
                    ["operation"] = Operation.Write.ToString(),
                    ["projectId"] = projectId,
                    ["reason"] = ex.Message,
                }),
                cancellationToken).ConfigureAwait(false);

            throw;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                errorCode = "FileUploadFailed",
                message = ex.Message,
            });
        }
    }

    /// <summary>
    /// 获取文件元数据。
    /// </summary>
    [HttpGet("{id:guid}")]
    [AuthorizePermission(Scope.File, Operation.Read)]
    public async Task<ActionResult<StoredFileDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var file = await fileService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return this.OkOrNotFound(file);
    }

    /// <summary>
    /// 下载文件。
    /// </summary>
    [HttpGet("{id:guid}/download")]
    [AuthorizePermission(Scope.File, Operation.Read)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var (stream, metadata) = await fileService.GetDownloadAsync(id, cancellationToken).ConfigureAwait(false);
        if (stream is null)
        {
            return NotFound();
        }

        var contentType = metadata?.ContentType ?? "application/octet-stream";
        var fileName = metadata?.FileName ?? "download";

        return File(stream, contentType, fileName);
    }

    /// <summary>
    /// 删除文件。
    /// </summary>
    [HttpDelete("{id:guid}")]
    [AuthorizePermission(Scope.File, Operation.Write)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await fileService.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }

    /// <summary>
    /// 获取项目下的所有文件。
    /// </summary>
    [HttpGet]
    [AuthorizePermission(Scope.File, Operation.Read)]
    public async Task<ActionResult<PagedResult<StoredFileDto>>> GetAll(
        [FromQuery] Guid projectId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var files = await fileService.GetAllByProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        var result = new PagedResult<StoredFileDto>
        {
            Items = files,
            TotalCount = files.Count,
            Page = page,
            PageSize = pageSize,
        };
        return Ok(result);
    }
}
