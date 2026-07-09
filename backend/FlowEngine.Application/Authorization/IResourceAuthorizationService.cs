using FlowEngine.Core.Authorization;

namespace FlowEngine.Application.Authorization;

/// <summary>
/// 资源级授权服务接口，按用户角色判定对具体资源的访问权限。
/// </summary>
public interface IResourceAuthorizationService
{
    /// <summary>
    /// 检查指定用户是否有权对目标工作流执行给定操作。
    /// </summary>
    Task<bool> CanAccessWorkflowAsync(Guid userId, Guid workflowId, Operation operation, CancellationToken ct = default);

    /// <summary>
    /// 检查指定用户是否有权对目标凭据执行给定操作。
    /// </summary>
    Task<bool> CanAccessCredentialAsync(Guid userId, Guid credentialId, Operation operation, CancellationToken ct = default);

    /// <summary>
    /// 检查指定用户是否有权对目标执行记录执行给定操作。
    /// </summary>
    Task<bool> CanAccessExecutionAsync(Guid userId, Guid executionId, Operation operation, CancellationToken ct = default);

    /// <summary>
    /// 检查指定用户是否有权对目标触发器执行给定操作。
    /// </summary>
    Task<bool> CanAccessTriggerAsync(Guid userId, Guid triggerId, Operation operation, CancellationToken ct = default);

    /// <summary>
    /// 裁定指定用户对目标资源的访问权限，区分角色不足（<see cref="AccessDecision.DeniedByRole"/>）
    /// 与资源归属不符（<see cref="AccessDecision.DeniedByOwnership"/>）。供 AuthorizationGuard 细化审计 reason。
    /// 默认实现委托各 <c>CanAccess*</c> 方法，保证既有桩/实现无需改动即可编译；
    /// 生产实现（ResourceAuthorizationService）已重写以返回精确的归属裁定。
    /// </summary>
    Task<AccessDecision> DecideAsync(Guid userId, ResourceKind kind, Guid resourceId, Operation operation, CancellationToken ct = default)
    {
        return DecideCoreAsync(userId, kind, resourceId, operation, ct);

        async Task<AccessDecision> DecideCoreAsync(Guid u, ResourceKind k, Guid r, Operation o, CancellationToken c)
        {
            var allowed = await (k switch
            {
                ResourceKind.Workflow => CanAccessWorkflowAsync(u, r, o, c),
                ResourceKind.Credential => CanAccessCredentialAsync(u, r, o, c),
                ResourceKind.Execution => CanAccessExecutionAsync(u, r, o, c),
                ResourceKind.Trigger => CanAccessTriggerAsync(u, r, o, c),
                ResourceKind.Project => CanAccessProjectAsync(u, r, o, c),
                ResourceKind.File => CanAccessProjectAsync(u, r, o, c),
                _ => Task.FromResult(false),
            }).ConfigureAwait(false);

            return allowed ? AccessDecision.Allowed : AccessDecision.DeniedByRole;
        }
    }

    /// <summary>
    /// 判断给定角色集合是否应对凭据敏感字段进行脱敏。
    /// </summary>
    bool ShouldMaskCredentialValues(IReadOnlyList<string> roles);

    /// <summary>
    /// 检查指定用户是否有权访问目标项目。
    /// </summary>
    Task<bool> CanAccessProjectAsync(Guid userId, Guid projectId, Operation operation, CancellationToken ct = default)
    {
        return Task.FromResult(false);
    }
}
