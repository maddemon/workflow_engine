using FlowEngine.Application.Audit;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Events;

namespace FlowEngine.Application.Authorization;

/// <summary>
/// 策略显式的授权操作处理器，封装查前/查后授权检查与审计事件发布。
/// 本质差异通过 <see cref="AuthorizationPolicy"/> 字段显式区分，偶然差异归一为查前 fail-fast。
/// </summary>
public sealed class AuthorizedOperationHandler(
    IAuthorizationGuard authGuard,
    IEventBus eventBus,
    AuditEventFactory auditFactory)
{
    /// <summary>
    /// 执行查前授权检查（偶然差异归一为 fail-fast）。
    /// 按 Policy 依次执行 RequireAccess → RequireScope → RequireAdmin。
    /// </summary>
    public async Task AuthorizePreAsync(AuthorizationPolicy policy, Guid resourceId, CancellationToken ct)
    {
        if (policy.Resource is not null && policy.Access is not null)
        {
            await authGuard.RequireAccessAsync(policy.Resource.Value, resourceId, policy.Access.Value, ct).ConfigureAwait(false);
        }
        if (policy.Scope is not null)
        {
            await authGuard.RequireScopeAsync(policy.Scope.Value, policy.Access ?? Operation.Write, ct).ConfigureAwait(false);
        }
        if (policy.AdminPhase)
        {
            await authGuard.RequireAdminAsync(policy.Access ?? Operation.Delete, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 执行项目级授权检查（查后，本质差异：先确认项目存在再检查权限）。
    /// </summary>
    public async Task AuthorizeProjectAccessAsync(Guid projectId, Operation operation, CancellationToken ct)
    {
        await authGuard.RequireAccessAsync(ResourceKind.Project, projectId, operation, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 发布审计事件（横切辅助方法，与编排解耦）。
    /// </summary>
    public async Task PublishAuditAsync(
        string eventType,
        string resourceType,
        Guid resourceId,
        Dictionary<string, object>? payload = null,
        CancellationToken ct = default)
    {
        await eventBus.PublishAsync(
            auditFactory.Create<AuditLogEvent>(eventType, resourceType, resourceId, payload),
            ct).ConfigureAwait(false);
    }
}
