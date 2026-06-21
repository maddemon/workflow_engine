using System.ComponentModel.DataAnnotations.Schema;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 节点错误信息。
/// </summary>
[NotMapped]
public class NodeError
{
    /// <summary>
    /// 错误码。
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 错误信息。
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 节点定义 ID。
    /// </summary>
    public Guid NodeDefinitionId { get; set; }

    /// <summary>
    /// 错误详情映射。
    /// </summary>
    public Dictionary<string, string> Details { get; set; } = [];

    /// <summary>
    /// 堆栈跟踪。
    /// </summary>
    public string? StackTrace { get; set; }
}
