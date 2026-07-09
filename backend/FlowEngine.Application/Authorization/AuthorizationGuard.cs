using FlowEngine.Application.Audit;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Authorization;
using FlowEngine.Core.Events;
using FlowEngine.Core.Exceptions;

namespace FlowEngine.Application.Authorization;

/// <summary>
/// <see cref="IAuthorizationGuard"/> 默认实现。
/// 注入 <see cref="IUserContext"/> 统一获取当前用户与角色，封装认证、授权与审计的不变量。
/// </summary>
public sealed class AuthorizationGuard(
    IUserContext userContext,
    IResourceAuthorizationService resourceAuthorization,
    IAuthorizationService authorizationService,
    IEventBus eventBus,
    AuditEventFactory auditFactory) : IAuthorizationGuard
{
    /// <inheritdoc />
    public async Task RequireAccessAsync(ResourceKind kind, Guid resourceId, Operation operation, CancellationToken ct = default)
    {
        var userId = RequireAuthenticated();

        var decision = await resourceAuthorization.DecideAsync(userId, kind, resourceId, operation, ct).ConfigureAwait(false);
        if (decision == AccessDecision.Allowed)
        {
            return;
        }

        // reason 细化为角色不足 / 归属不符，辅助审计定位越权原因。
        var reason = decision == AccessDecision.DeniedByRole ? "role" : "ownership";
        await PublishDeniedAsync(kind.ToString(), resourceId, operation, reason, ct).ConfigureAwait(false);
        throw new PermissionDeniedException($"当前用户没有{Describe(operation)}该{Describe(kind)}的权限。");
    }

    /// <inheritdoc />
    public async Task RequireScopeAsync(Scope scope, Operation operation, CancellationToken ct = default)
    {
        RequireAuthenticated();

        if (!authorizationService.HasPermission(userContext.Roles, scope, operation))
        {
            await PublishDeniedAsync(scope.ToString(), Guid.Empty, operation, "role", ct).ConfigureAwait(false);
            throw new PermissionDeniedException($"当前用户没有{Describe(operation)}的权限。");
        }
    }

    /// <inheritdoc />
    public async Task RequireAdminAsync(Operation operation, CancellationToken ct = default)
    {
        RequireAuthenticated();

        if (!userContext.Roles.Contains(RoleConstants.Admin, StringComparer.OrdinalIgnoreCase))
        {
            await PublishDeniedAsync("System", Guid.Empty, operation, "role", ct).ConfigureAwait(false);
            throw new PermissionDeniedException("仅管理员可执行该操作。");
        }
    }

    private Guid RequireAuthenticated()
    {
        var userId = userContext.UserId;
        if (userId is null)
        {
            // 未认证 → 401（语义正确，区别于权限不足的 403）。
            throw new UnauthorizedException("当前用户未认证。");
        }

        return userId.Value;
    }

    private async Task PublishDeniedAsync(string resourceType, Guid resourceId, Operation operation, string reason, CancellationToken ct)
    {
        await eventBus.PublishAsync(auditFactory.Create<AuditLogEvent>(
            AuditEventTypes.PermissionDenied,
            resourceType,
            resourceId,
            new Dictionary<string, object> { ["operation"] = operation.ToString(), ["reason"] = reason }),
            ct).ConfigureAwait(false);
    }

    private static string Describe(Operation operation) => operation switch
    {
        Operation.Read => "读取",
        Operation.Write => "修改",
        Operation.Execute => "执行",
        Operation.Delete => "删除",
        _ => operation.ToString(),
    };

    private static string Describe(ResourceKind kind) => kind switch
    {
        ResourceKind.Workflow => "工作流",
        ResourceKind.Credential => "凭据",
        ResourceKind.Execution => "执行记录",
        ResourceKind.Trigger => "触发器",
        ResourceKind.Project => "项目",
        ResourceKind.File => "文件",
        _ => kind.ToString(),
    };
}
