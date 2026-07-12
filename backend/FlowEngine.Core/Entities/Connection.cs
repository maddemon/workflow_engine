using System.ComponentModel.DataAnnotations.Schema;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 节点连接。
/// </summary>
[NotMapped]
public class Connection : Entity
{
    /// <summary>
    /// 源节点实例 ID（引用 NodeDefinition.Id 的字符串标识）。
    /// </summary>
    public string SourceNodeId { get; set; } = string.Empty;

    /// <summary>
    /// 源端口名称。缺省时由执行引擎取源节点第一个 Output 端口。
    /// </summary>
    public string? SourcePortName { get; set; }

    /// <summary>
    /// 目标节点实例 ID（引用 NodeDefinition.Id 的字符串标识）。
    /// </summary>
    public string TargetNodeId { get; set; } = string.Empty;

    /// <summary>
    /// 目标端口名称。缺省时由执行引擎取目标节点第一个 Input 端口。
    /// </summary>
    public string? TargetPortName { get; set; }

    /// <summary>
    /// 连接条件表达式。
    /// </summary>
    public string? Condition { get; set; }
}
