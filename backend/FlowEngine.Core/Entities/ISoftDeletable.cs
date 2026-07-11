namespace FlowEngine.Core.Entities;

/// <summary>
/// 支持软删除的实体接口。
/// </summary>
public interface ISoftDeletable
{
    bool Deleted { get; set; }
    DateTime? UpdatedAt { get; set; }
}
