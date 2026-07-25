using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Events;
using FlowEngine.Core.Entities;

namespace FlowEngine.Runtime.Credentials;

/// <summary>
/// 凭据访问审计装饰器：在运行时凭据解析（解密）成功后发布 <see cref="CredentialAccessedEvent"/>，
/// 闭合凭据访问审计链（OBS-1）。仅包裹真实执行路径的访问器，不接触凭据明文值之外的敏感数据。
/// </summary>
internal sealed class CredentialAuditAccessor : ICredentialAccessor
{
    private readonly ICredentialAccessor _inner;
    private readonly IEventBus? _eventBus;
    private readonly Guid _executionId;
    private readonly string _nodeDefinitionId;

    public CredentialAuditAccessor(
        ICredentialAccessor inner,
        IEventBus? eventBus,
        Guid executionId,
        string nodeDefinitionId)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _eventBus = eventBus;
        _executionId = executionId;
        _nodeDefinitionId = nodeDefinitionId;
    }

    public async Task<CredentialValue> GetCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
    {
        var value = await _inner.GetCredentialAsync(credentialId, cancellationToken).ConfigureAwait(false);
        PublishIfNeeded(value);
        return value;
    }

    public async Task<CredentialValue?> GetCredentialByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var value = await _inner.GetCredentialByNameAsync(name, cancellationToken).ConfigureAwait(false);
        PublishIfNeeded(value);
        return value;
    }

    private void PublishIfNeeded(CredentialValue? value)
    {
        if (_eventBus is null || value is null)
        {
            return;
        }

        // 记录凭据访问（含凭据 ID、执行 ID、节点定义 ID），不直接记录凭据明文。
        // AccessType 固定为 "Resolve" 表示运行时解析/解密访问。
        var auditEvent = new CredentialAccessedEvent(value.Id, _executionId, _nodeDefinitionId, "Resolve");
        _ = _eventBus.PublishAsync(auditEvent);
    }
}
