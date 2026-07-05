using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Nodes;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 数据项。
/// </summary>
[NotMapped]
public class DataItem
{
    /// <summary>
    /// JSON 数据。
    /// </summary>
    public JsonNode? Data { get; set; }

    /// <summary>
    /// 是否成功。
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 错误信息。
    /// </summary>
    public NodeError? Error { get; set; }

    /// <summary>
    /// 来源索引。
    /// </summary>
    public int SourceIndex { get; set; }

    /// <summary>
    /// 关联的已存储文件 ID。节点间传递文件时使用 AttachmentId 引用，避免将大文件二进制直接写入执行记录。
    /// </summary>
    public Guid? AttachmentId { get; set; }
}
