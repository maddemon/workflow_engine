using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 节点执行记录。
/// </summary>
[Table("node_execution_records", Schema = "flow")]
[Comment("节点执行记录")]
public class NodeExecutionRecord : Entity
{
    /// <summary>
    /// 节点定义 ID。
    /// </summary>
    [Column("node_definition_id")]
    [Comment("节点定义 ID")]
    public Guid NodeDefinitionId { get; set; }

    /// <summary>
    /// 运行索引。
    /// </summary>
    [Column("run_index")]
    [Comment("运行索引")]
    public int RunIndex { get; set; }

    /// <summary>
    /// 开始时间。
    /// </summary>
    [Column("started_at")]
    [Comment("开始时间")]
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// 完成时间。
    /// </summary>
    [Column("completed_at")]
    [Comment("完成时间")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// 输入数据批次映射。
    /// </summary>
    [Column("inputs")]
    [Comment("输入数据批次映射")]
    public IReadOnlyDictionary<string, DataBatch> Inputs { get; set; } = new Dictionary<string, DataBatch>();

    /// <summary>
    /// 节点执行结果。
    /// </summary>
    [Column("output")]
    [Comment("节点执行结果")]
    public NodeExecutionResult Output { get; set; } = new();

    /// <summary>
    /// 原始参数映射。
    /// </summary>
    [Column("raw_parameters")]
    [Comment("原始参数映射")]
    public IReadOnlyDictionary<string, object> RawParameters { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// 解析后的参数映射。
    /// </summary>
    [Column("resolved_parameters")]
    [Comment("解析后的参数映射")]
    public IReadOnlyDictionary<string, object> ResolvedParameters { get; set; } = new Dictionary<string, object>();
}
