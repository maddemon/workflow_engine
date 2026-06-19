namespace FlowEngine.Core.Events;

/// <summary>
/// 凭据访问事件。
/// </summary>
/// <param name="EventId">事件 ID。</param>
/// <param name="OccurredAt">事件发生时间。</param>
/// <param name="CredentialId">凭据 ID。</param>
/// <param name="ExecutionId">执行 ID。</param>
/// <param name="NodeDefinitionId">节点定义 ID。</param>
/// <param name="AccessType">访问类型。</param>
public record CredentialAccessedEvent(
    Guid EventId,
    DateTime OccurredAt,
    Guid CredentialId,
    Guid ExecutionId,
    Guid NodeDefinitionId,
    string AccessType)
    : AuditEvent(EventId, OccurredAt)
{
    /// <summary>
    /// 初始化凭据访问事件。
    /// </summary>
    /// <param name="credentialId">凭据 ID。</param>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="nodeDefinitionId">节点定义 ID。</param>
    /// <param name="accessType">访问类型。</param>
    public CredentialAccessedEvent(
        Guid credentialId,
        Guid executionId,
        Guid nodeDefinitionId,
        string accessType)
        : this(
            Guid.NewGuid(),
            DateTime.UtcNow,
            credentialId,
            executionId,
            nodeDefinitionId,
            accessType)
    {
    }
}
