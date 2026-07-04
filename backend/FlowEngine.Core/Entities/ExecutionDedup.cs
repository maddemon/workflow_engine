using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace FlowEngine.Core.Entities;

[Table("execution_dedup", Schema = "flow")]
[Comment("执行幂等去重表")]
public class ExecutionDedup
{
    [Key]
    [Required]
    [MaxLength(512)]
    [Column("idempotency_key")]
    [Comment("幂等键")]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Required]
    [Column("execution_id")]
    [Comment("执行记录 ID")]
    public Guid ExecutionId { get; set; }

    [Required]
    [Column("created_at")]
    [Comment("创建时间")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("expires_at")]
    [Comment("过期时间")]
    public DateTime? ExpiresAt { get; set; }
}
