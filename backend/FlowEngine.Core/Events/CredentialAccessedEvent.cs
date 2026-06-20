namespace FlowEngine.Core.Events;

/// <summary>
/// 凭据访问事件。
/// </summary>
public record CredentialAccessedEvent : AuditEvent
{
    /// <summary>
    /// 凭据 ID。
    /// </summary>
    public Guid CredentialId { get; init; }

    /// <summary>
    /// 执行 ID。
    /// </summary>
    public Guid ExecutionId { get; init; }

    /// <summary>
    /// 节点定义 ID。
    /// </summary>
    public Guid NodeDefinitionId { get; init; }

    /// <summary>
    /// 访问类型。
    /// </summary>
    public string AccessType { get; init; } = string.Empty;

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
    {
        CredentialId = credentialId;
        ExecutionId = executionId;
        NodeDefinitionId = nodeDefinitionId;
        AccessType = accessType;
        EventType = AuditEventTypes.CredentialAccessed;
        ResourceType = "Credential";
        ResourceId = credentialId;
    }
}
