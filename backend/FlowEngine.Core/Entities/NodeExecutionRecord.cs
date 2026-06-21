using System.ComponentModel.DataAnnotations.Schema;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 节点执行记录（POJO）。
/// </summary>
[NotMapped]
public class NodeExecutionRecord
{
    /// <summary>
    /// 主键。
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 节点定义 ID。
    /// </summary>
    public Guid NodeDefinitionId { get; set; }

    /// <summary>
    /// 运行索引。
    /// </summary>
    public int RunIndex { get; set; }

    /// <summary>
    /// 开始时间。
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// 完成时间。
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// 输入数据批次映射。
    /// </summary>
    public Dictionary<string, DataBatch> Inputs { get; set; } = [];

    /// <summary>
    /// 节点执行结果。
    /// </summary>
    public NodeExecutionResult Output { get; set; } = new();

    /// <summary>
    /// 原始参数映射。
    /// </summary>
    public Dictionary<string, object> RawParameters { get; set; } = [];

    /// <summary>
    /// 解析后的参数映射。
    /// </summary>
    public Dictionary<string, object> ResolvedParameters { get; set; } = [];
}
