using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FlowEngine.Core.Entities;

public interface IEntity<T>
{
    [Key]
    [Column("id")]
    [Comment("主键")]
    T Id { get; set; }
}


/// <summary>
/// 实体基类，提供有序主键生成。
/// </summary>
public abstract class Entity : IEntity<Guid>
{
    public Entity()
    {
        CreatedAt = DateTime.UtcNow;
        Id = NewId(CreatedAt);
    }
    private static Guid NewId(DateTimeOffset? timestamp = null) => Guid.CreateVersion7(timestamp ?? DateTimeOffset.UtcNow);

    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    [MaxLength(36), StringLength(36)]
    public Guid Id { get; set; }

    [Comment("创建时间")]
    public DateTime CreatedAt { get; set; }

    [Comment("最后更新时间")]
    public DateTime? UpdatedAt { get; set; }

    [Comment("是否删除")]
    public bool Deleted { get; set; }
}