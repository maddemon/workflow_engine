namespace FlowEngine.Application.Audit;

/// <summary>
/// 审计日志查询参数。
/// </summary>
public sealed class AuditQueryParameters
{
    /// <summary>
    /// 事件类型过滤。
    /// </summary>
    public string? EventType { get; set; }

    /// <summary>
    /// 起始时间。
    /// </summary>
    public DateTime? From { get; set; }

    /// <summary>
    /// 结束时间。
    /// </summary>
    public DateTime? To { get; set; }

    /// <summary>
    /// 资源类型过滤。
    /// </summary>
    public string? ResourceType { get; set; }

    /// <summary>
    /// 资源 ID 过滤。
    /// </summary>
    public Guid? ResourceId { get; set; }

    /// <summary>
    /// 分页偏移量。
    /// </summary>
    public int Offset { get; set; }

    private int _limit = 50;

    /// <summary>
    /// 分页大小。
    /// </summary>
    public int Limit
    {
        get => _limit;
        set => _limit = Math.Clamp(value, 1, 200);
    }
}
