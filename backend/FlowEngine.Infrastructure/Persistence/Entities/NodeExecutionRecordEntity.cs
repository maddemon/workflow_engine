using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FlowEngine.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Infrastructure.Persistence.Entities;

/// <summary>
/// 节点执行记录数据库实体。
/// </summary>
[Table("node_execution_records")]
[Comment("节点执行记录")]
[Index(nameof(ExecutionId))]
public sealed class NodeExecutionRecordEntity : Entity
{
    /// <summary>
    /// 执行 ID。
    /// </summary>
    [Column("execution_id")]
    [Comment("执行 ID")]
    public Guid ExecutionId { get; set; }

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
    /// 输入数据批次映射 JSON。
    /// </summary>
    [Column("inputs")]
    [Comment("输入数据批次映射 JSON")]
    public string InputsJson { get; set; } = "{}";

    /// <summary>
    /// 节点执行结果 JSON。
    /// </summary>
    [Column("output")]
    [Comment("节点执行结果 JSON")]
    public string OutputJson { get; set; } = "{}";

    /// <summary>
    /// 原始参数映射 JSON。
    /// </summary>
    [Column("raw_parameters")]
    [Comment("原始参数映射 JSON")]
    public string RawParametersJson { get; set; } = "{}";

    /// <summary>
    /// 解析后的参数映射 JSON。
    /// </summary>
    [Column("resolved_parameters")]
    [Comment("解析后的参数映射 JSON")]
    public string ResolvedParametersJson { get; set; } = "{}";

    /// <summary>
    /// 关联的执行记录。
    /// </summary>
    [ForeignKey(nameof(ExecutionId))]
    public ExecutionRecordEntity Execution { get; set; } = null!;
}
