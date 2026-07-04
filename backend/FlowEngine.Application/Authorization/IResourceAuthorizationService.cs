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
    /// 判断给定角色集合是否应对凭据敏感字段进行脱敏。
    /// </summary>
    bool ShouldMaskCredentialValues(IReadOnlyList<string> roles);
}
