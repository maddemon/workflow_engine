using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 存储文件实体，记录上传文件的元数据。
/// </summary>
[Table("stored_files", Schema = "flow")]
[Comment("存储文件")]
public class StoredFile : Entity
{
    /// <summary>
    /// 原始文件名。
    /// </summary>
    [Required]
    [MaxLength(256)]
    [Column("file_name")]
    [Comment("文件名")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// MIME 类型。
    /// </summary>
    [MaxLength(128)]
    [Column("content_type")]
    [Comment("MIME 类型")]
    public string? ContentType { get; set; }

    /// <summary>
    /// 文件大小（字节）。
    /// </summary>
    [Required]
    [Column("size")]
    [Comment("文件大小")]
    public long Size { get; set; }

    /// <summary>
    /// 存储路径。
    /// </summary>
    [Required]
    [MaxLength(1024)]
    [Column("storage_path")]
    [Comment("存储路径")]
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>
    /// 所属项目 ID。
    /// </summary>
    [Required]
    [Column("project_id")]
    [Comment("所属项目")]
    public Guid ProjectId { get; set; }

    /// <summary>
    /// 上传者用户 ID。
    /// </summary>
    [Required]
    [Column("uploaded_by")]
    [Comment("上传者")]
    public Guid UploadedBy { get; set; }
}
