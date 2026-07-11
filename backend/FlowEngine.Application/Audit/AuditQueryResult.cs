using System.Text.Json;

namespace FlowEngine.Application.Audit;

/// <summary>
/// 审计日志查询结果。
/// </summary>
public sealed class AuditQueryResult
{
    /// <summary>
    /// 事件列表。
    /// </summary>
    public IReadOnlyList<JsonDocument> Events { get; init; } = [];

    /// <summary>
    /// 总匹配数。
    /// </summary>
    public int Total { get; init; }
}
