using System.ComponentModel.DataAnnotations.Schema;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 节点连接。
/// </summary>
[NotMapped]
public class Connection : Entity
{
    /// <summary>
    /// 源节点实例 ID。
    /// </summary>
    public Guid SourceNodeId { get; set; }

    /// <summary>
    /// 源端口名称。
    /// </summary>
    public string SourcePortName { get; set; } = string.Empty;

    /// <summary>
    /// 目标节点实例 ID。
    /// </summary>
    public Guid TargetNodeId { get; set; }

    /// <summary>
    /// 目标端口名称。
    /// </summary>
    public string TargetPortName { get; set; } = string.Empty;

    /// <summary>
    /// 连接条件表达式。
    /// </summary>
    public string? Condition { get; set; }
}
