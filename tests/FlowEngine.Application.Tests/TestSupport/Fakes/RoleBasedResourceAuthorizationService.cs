using FlowEngine.Application.Authorization;
using FlowEngine.Application.Identity;
using FlowEngine.Core.Authorization;

namespace FlowEngine.Application.Tests.TestSupport.Fakes;

public sealed class RoleBasedResourceAuthorizationService(IUserContext userContext) : IResourceAuthorizationService
{
    public Task<bool> CanAccessWorkflowAsync(Guid userId, Guid workflowId, Operation operation, CancellationToken ct = default)
        => Task.FromResult(HasPermission(Scope.Workflow, operation));

    public Task<bool> CanAccessCredentialAsync(Guid userId, Guid credentialId, Operation operation, CancellationToken ct = default)
        => Task.FromResult(HasPermission(Scope.Credential, operation));

    public Task<bool> CanAccessExecutionAsync(Guid userId, Guid executionId, Operation operation, CancellationToken ct = default)
        => Task.FromResult(HasPermission(Scope.Execution, operation));

    public Task<bool> CanAccessTriggerAsync(Guid userId, Guid triggerId, Operation operation, CancellationToken ct = default)
        => Task.FromResult(HasPermission(Scope.Trigger, operation));

    public bool ShouldMaskCredentialValues(IReadOnlyList<string> roles) => false;

    private bool HasPermission(Scope scope, Operation operation)
    {
        foreach (var roleStr in userContext.Roles)
        {
            if (Enum.TryParse<Role>(roleStr, ignoreCase: true, out var role) && PermissionMapping.HasPermission(role, scope, operation))
            {
                return true;
            }
        }

        return false;
    }
}
