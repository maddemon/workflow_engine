using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 工作流定义。
/// </summary>
[Table("workflows", Schema = "flow")]
[Comment("工作流定义")]
public class Workflow : Entity
{
    /// <summary>
    /// 项目 ID。
    /// </summary>
    [Column("project_id")]
    [Comment("项目 ID")]
    public Guid? ProjectId { get; set; }

    /// <summary>
    /// 工作流名称。
    /// </summary>
    [Required]
    [MaxLength(256)]
    [Column("name")]
    [Comment("工作流名称")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 版本号。
    /// </summary>
    [Column("version")]
    [Comment("版本号")]
    public int Version { get; set; }

    /// <summary>
    /// 创建人。
    /// </summary>
    [Required]
    [MaxLength(256)]
    [Column("created_by")]
    [Comment("创建人")]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// 节点实例列表。
    /// </summary>
    [Column("nodes")]
    [Comment("节点实例列表")]
    [JsonColumn]
    public List<NodeDefinition> Nodes { get; set; } = [];

    /// <summary>
    /// 连接列表。
    /// </summary>
    [Column("connections")]
    [Comment("连接列表")]
    [JsonColumn]
    public List<Connection> Connections { get; set; } = [];

    /// <summary>
    /// 是否激活。
    /// </summary>
    [Column("is_active")]
    [Comment("是否激活")]
    public bool IsActive { get; set; }

    /// <summary>
    /// 工作流来源。
    /// </summary>
    [Column("source")]
    [Comment("工作流来源：人工创建或 AI 生成")]
    public WorkflowSource Source { get; set; }

    /// <summary>
    /// 草稿审查状态。
    /// </summary>
    [Column("draft_status")]
    [Comment("草稿审查状态：待审查/已拒绝/已确认")]
    public DraftStatus? DraftStatus { get; set; }

    /// <summary>
    /// 拒绝理由（仅 DraftStatus=Rejected 时有值）。
    /// </summary>
    [MaxLength(2000)]
    [Column("rejection_reason")]
    [Comment("拒绝理由")]
    public string? RejectionReason { get; set; }

    /// <summary>
    /// modify 草稿的结构化差异列表；assemble 新建草稿为空。
    /// </summary>
    [Column("diff")]
    [Comment("modify 草稿的结构化差异")]
    [JsonColumn]
    public List<StructuredDiff> Diff { get; set; } = [];

    /// <summary>
    /// 样式设置，如布局方向等。
    /// </summary>
    [Column("style_settings")]
    [Comment("样式设置")]
    [JsonColumn]
    public WorkflowStyleSettings? StyleSettings { get; set; }
}
