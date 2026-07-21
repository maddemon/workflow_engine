using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 执行记录。
/// </summary>
[Table("execution_records", Schema = "flow")]
[Comment("执行记录")]
// P3 #2：热查询列补索引，避免执行列表/清理/按工作流查询全表扫描。
[Index(nameof(WorkflowDefinitionId))]
[Index(nameof(ProjectId))]
[Index(nameof(Status), nameof(CompletedAt))]
public class ExecutionRecord : Entity
{
    /// <summary>
    /// 工作流定义 ID。
    /// </summary>
    [Column("workflow_definition_id")]
    [Comment("工作流定义 ID")]
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>
    /// 项目 ID（冗余字段，便于直接按项目隔离查询，GAP-11）。
    /// </summary>
    [Column("project_id")]
    [Comment("项目 ID")]
    public Guid? ProjectId { get; set; }

    /// <summary>
    /// 父执行 ID。
    /// </summary>
    [Column("parent_execution_id")]
    [Comment("父执行 ID")]
    public Guid? ParentExecutionId { get; set; }

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
    /// 执行状态。
    /// </summary>
    [Column("status")]
    [Comment("执行状态")]
    public ExecutionStatus Status { get; set; }

    /// <summary>
    /// 节点执行记录列表。
    /// </summary>
    [Column("node_records")]
    [Comment("节点执行记录列表")]
    [JsonColumn]
    public List<NodeExecutionRecord> NodeRecords { get; set; } = [];
}
