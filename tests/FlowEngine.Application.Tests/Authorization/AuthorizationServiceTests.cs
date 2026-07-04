using FlowEngine.Application.Authorization;
using FlowEngine.Core.Authorization;

namespace FlowEngine.Application.Tests.Authorization;

public class AuthorizationServiceTests
{
    private readonly IAuthorizationService _sut = new AuthorizationService();

    [Fact]
    public void HasPermission_AdminRole_HasAllPermissions()
    {
        var roles = new List<string> { "Admin" };

        Assert.True(_sut.HasPermission(roles, Scope.Workflow, Operation.Read));
        Assert.True(_sut.HasPermission(roles, Scope.Workflow, Operation.Write));
        Assert.True(_sut.HasPermission(roles, Scope.Workflow, Operation.Delete));
        Assert.True(_sut.HasPermission(roles, Scope.Credential, Operation.Read));
        Assert.True(_sut.HasPermission(roles, Scope.Execution, Operation.Execute));
    }

    [Fact]
    public void HasPermission_ViewerRole_OnlyReadPermissions()
    {
        var roles = new List<string> { "Viewer" };

        Assert.True(_sut.HasPermission(roles, Scope.Workflow, Operation.Read));
        Assert.False(_sut.HasPermission(roles, Scope.Workflow, Operation.Write));
        Assert.False(_sut.HasPermission(roles, Scope.Workflow, Operation.Delete));
        Assert.False(_sut.HasPermission(roles, Scope.Workflow, Operation.Execute));
    }

    [Fact]
    public void HasPermission_EditorRole_WorkflowReadWriteExecute()
    {
        var roles = new List<string> { "Editor" };

        Assert.True(_sut.HasPermission(roles, Scope.Workflow, Operation.Read));
        Assert.True(_sut.HasPermission(roles, Scope.Workflow, Operation.Write));
        Assert.True(_sut.HasPermission(roles, Scope.Workflow, Operation.Execute));
        Assert.False(_sut.HasPermission(roles, Scope.Workflow, Operation.Delete));
    }

    [Fact]
    public void HasPermission_InvalidRoleString_ReturnsFalse()
    {
        var roles = new List<string> { "SuperAdmin" };

        Assert.False(_sut.HasPermission(roles, Scope.Workflow, Operation.Read));
    }

    [Fact]
    public void HasPermission_EmptyRoles_ReturnsFalse()
    {
        var roles = new List<string>();

        Assert.False(_sut.HasPermission(roles, Scope.Workflow, Operation.Read));
    }

    [Fact]
    public void HasPermission_CaseInsensitiveRole_ReturnsTrue()
    {
        var roles = new List<string> { "admin" };

        Assert.True(_sut.HasPermission(roles, Scope.Workflow, Operation.Read));
    }

    [Fact]
    public void HasPermission_MultipleRoles_AnyMatchSucceeds()
    {
        var roles = new List<string> { "Viewer", "Editor" };

        Assert.True(_sut.HasPermission(roles, Scope.Credential, Operation.Write));
    }

    [Fact]
    public void GetAllowedScopes_Admin_AllScopes()
    {
        var roles = new List<string> { "Admin" };

        var scopes = _sut.GetAllowedScopes(roles, Operation.Read);

        Assert.Equal(Enum.GetValues<Scope>().Length, scopes.Count);
    }

    [Fact]
    public void GetAllowedScopes_Viewer_AllScopesForRead()
    {
        var roles = new List<string> { "Viewer" };

        var scopes = _sut.GetAllowedScopes(roles, Operation.Read);

        Assert.Equal(Enum.GetValues<Scope>().Length, scopes.Count);
    }

    [Fact]
    public void GetAllowedScopes_Viewer_NoScopesForWrite()
    {
        var roles = new List<string> { "Viewer" };

        var scopes = _sut.GetAllowedScopes(roles, Operation.Write);

        Assert.Empty(scopes);
    }

    [Fact]
    public void GetAllowedScopes_EmptyRoles_EmptyResult()
    {
        var roles = new List<string>();

        var scopes = _sut.GetAllowedScopes(roles, Operation.Read);

        Assert.Empty(scopes);
    }
}
