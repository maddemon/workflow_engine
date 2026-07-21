using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 工作流→凭据引用关系（归一化关联表）。
/// 由 <see cref="FlowEngineDbContext.SaveChangesAsync"/> 在工作流新增/修改/删除时集中维护，
/// 用于删除凭据时快速定位引用方，避免全表加载工作流 JSON 列。
/// </summary>
[Table("workflow_credential_usages", Schema = "flow")]
[Comment("工作流→凭据引用关系（归一化关联表），用于删除凭据时快速定位引用方")]
[PrimaryKey(nameof(WorkflowId), nameof(CredentialId), nameof(NodeId))]
[Index(nameof(CredentialId))]
public sealed class WorkflowCredentialUsage
{
    /// <summary>
    /// 所属工作流 ID。
    /// </summary>
    [Comment("所属工作流 ID")]
    public Guid WorkflowId { get; set; }

    /// <summary>
    /// 被引用凭据 ID。
    /// </summary>
    [Comment("被引用凭据 ID")]
    public Guid CredentialId { get; set; }

    /// <summary>
    /// 所属工作流名称（冗余存储，便于删除凭据时直接展示引用方，无需回查工作流表）。
    /// </summary>
    [Comment("所属工作流名称（冗余存储，便于删除凭据时直接展示引用方，无需回查工作流表）")]
    public string WorkflowName { get; set; } = string.Empty;

    /// <summary>
    /// 引用该凭据的节点 ID（工作流级引用时为空字符串）。
    /// </summary>
    [MaxLength(256)]
    [Comment("引用该凭据的节点 ID（工作流级引用时为空字符串）")]
    public string NodeId { get; set; } = string.Empty;
}
