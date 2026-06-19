using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Infrastructure.Persistence.Entities;

/// <summary>
/// 执行记录数据库实体。
/// </summary>
[Table("execution_records")]
[Comment("执行记录")]
[Index(nameof(WorkflowDefinitionId))]
[Index(nameof(Status))]
public sealed class ExecutionRecordEntity : Entity
{
    /// <summary>
    /// 工作流定义 ID。
    /// </summary>
    [Column("workflow_definition_id")]
    [Comment("工作流定义 ID")]
    public Guid WorkflowDefinitionId { get; set; }

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
    /// 节点执行记录列表 JSON。
    /// </summary>
    [Column("node_records")]
    [Comment("节点执行记录列表 JSON")]
    public string NodeRecordsJson { get; set; } = "[]";

    /// <summary>
    /// 关联的节点执行记录。
    /// </summary>
    [InverseProperty(nameof(NodeExecutionRecordEntity.Execution))]
    public ICollection<NodeExecutionRecordEntity> NodeExecutions { get; set; } = [];
}
