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
}
