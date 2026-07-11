namespace FlowEngine.Application.Audit;

/// <summary>
/// 审计日志读取器接口，由 Infrastructure 实现，供 Host 层通过依赖注入使用。
/// </summary>
public interface IAuditLogReader
{
    /// <summary>
    /// 按条件查询审计事件。
    /// </summary>
    /// <param name="parameters">查询参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>查询结果。</returns>
    Task<AuditQueryResult> QueryAsync(
        AuditQueryParameters parameters,
        CancellationToken cancellationToken = default);
}
