using FlowEngine.Core.Authorization;

namespace FlowEngine.Core.Tests.Authorization;

/// <summary>
/// PermissionMapping 单元测试。
/// </summary>
public class PermissionMappingTests
{
    /// <summary>
    /// 管理员在所有作用域下拥有所有操作权限。
    /// </summary>
    [Fact]
    public void HasPermission_AdminAllScopes_ReturnsTrue()
    {
        // Arrange
        var roles = Enum.GetValues<Role>();
        var scopes = Enum.GetValues<Scope>();
        var operations = Enum.GetValues<Operation>();

        // Act & Assert
        foreach (var role in roles)
        {
            foreach (var scope in scopes)
            {
                foreach (var operation in operations)
                {
                    if (role == Role.Admin)
                    {
                        Assert.True(
                            PermissionMapping.HasPermission(role, scope, operation),
                            $"Admin should have {operation} permission on {scope}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// 编辑者工作流权限正确。
    /// </summary>
    [Fact]
    public void HasPermission_EditorWorkflow_ReturnsCorrectPermissions()
    {
        // Arrange & Act & Assert
        Assert.True(PermissionMapping.HasPermission(Role.Editor, Scope.Workflow, Operation.Read));
        Assert.True(PermissionMapping.HasPermission(Role.Editor, Scope.Workflow, Operation.Write));
        Assert.True(PermissionMapping.HasPermission(Role.Editor, Scope.Workflow, Operation.Execute));
        Assert.False(PermissionMapping.HasPermission(Role.Editor, Scope.Workflow, Operation.Delete));
    }

    /// <summary>
    /// 编辑者凭据权限正确。
    /// </summary>
    [Fact]
    public void HasPermission_EditorCredential_ReturnsCorrectPermissions()
    {
        // Arrange & Act & Assert
        Assert.True(PermissionMapping.HasPermission(Role.Editor, Scope.Credential, Operation.Read));
        Assert.True(PermissionMapping.HasPermission(Role.Editor, Scope.Credential, Operation.Write));
        Assert.False(PermissionMapping.HasPermission(Role.Editor, Scope.Credential, Operation.Execute));
        Assert.False(PermissionMapping.HasPermission(Role.Editor, Scope.Credential, Operation.Delete));
    }

    /// <summary>
    /// 编辑者执行权限正确。
    /// </summary>
    [Fact]
    public void HasPermission_EditorExecution_ReturnsCorrectPermissions()
    {
        // Arrange & Act & Assert
        Assert.True(PermissionMapping.HasPermission(Role.Editor, Scope.Execution, Operation.Read));
        Assert.False(PermissionMapping.HasPermission(Role.Editor, Scope.Execution, Operation.Write));
        Assert.True(PermissionMapping.HasPermission(Role.Editor, Scope.Execution, Operation.Execute));
        Assert.False(PermissionMapping.HasPermission(Role.Editor, Scope.Execution, Operation.Delete));
    }

    /// <summary>
    /// 编辑者触发器权限正确。
    /// </summary>
    [Fact]
    public void HasPermission_EditorTrigger_ReturnsCorrectPermissions()
    {
        // Arrange & Act & Assert
        Assert.True(PermissionMapping.HasPermission(Role.Editor, Scope.Trigger, Operation.Read));
        Assert.True(PermissionMapping.HasPermission(Role.Editor, Scope.Trigger, Operation.Write));
        Assert.False(PermissionMapping.HasPermission(Role.Editor, Scope.Trigger, Operation.Execute));
        Assert.False(PermissionMapping.HasPermission(Role.Editor, Scope.Trigger, Operation.Delete));
    }

    /// <summary>
    /// 编辑者项目权限正确。
    /// </summary>
    [Fact]
    public void HasPermission_EditorProject_ReturnsCorrectPermissions()
    {
        // Arrange & Act & Assert
        Assert.True(PermissionMapping.HasPermission(Role.Editor, Scope.Project, Operation.Read));
        Assert.True(PermissionMapping.HasPermission(Role.Editor, Scope.Project, Operation.Write));
        Assert.False(PermissionMapping.HasPermission(Role.Editor, Scope.Project, Operation.Execute));
        Assert.False(PermissionMapping.HasPermission(Role.Editor, Scope.Project, Operation.Delete));
    }

    /// <summary>
    /// 编辑者用户权限正确。
    /// </summary>
    [Fact]
    public void HasPermission_EditorUser_ReturnsCorrectPermissions()
    {
        // Arrange & Act & Assert
        Assert.True(PermissionMapping.HasPermission(Role.Editor, Scope.User, Operation.Read));
        Assert.False(PermissionMapping.HasPermission(Role.Editor, Scope.User, Operation.Write));
        Assert.False(PermissionMapping.HasPermission(Role.Editor, Scope.User, Operation.Execute));
        Assert.False(PermissionMapping.HasPermission(Role.Editor, Scope.User, Operation.Delete));
    }

    /// <summary>
    /// 查看者只有读取权限。
    /// </summary>
    [Fact]
    public void HasPermission_ViewerReadOnly_ReturnsCorrectPermissions()
    {
        // Arrange
        var scopes = Enum.GetValues<Scope>();

        // Act & Assert
        foreach (var scope in scopes)
        {
            Assert.True(PermissionMapping.HasPermission(Role.Viewer, scope, Operation.Read));
            Assert.False(PermissionMapping.HasPermission(Role.Viewer, scope, Operation.Write));
            Assert.False(PermissionMapping.HasPermission(Role.Viewer, scope, Operation.Execute));
            Assert.False(PermissionMapping.HasPermission(Role.Viewer, scope, Operation.Delete));
        }
    }

    /// <summary>
    /// 不存在的组合返回默认拒绝。
    /// </summary>
    [Fact]
    public void HasPermission_InvalidRole_ReturnsFalse()
    {
        // Arrange
        var invalidRole = (Role)999;

        // Act & Assert
        Assert.False(PermissionMapping.HasPermission(invalidRole, Scope.Workflow, Operation.Read));
    }

    /// <summary>
    /// GetPermissions 返回正确的权限列表。
    /// </summary>
    [Fact]
    public void GetPermissions_Admin_ReturnsAllPermissions()
    {
        // Arrange
        var expectedScopeCount = Enum.GetValues<Scope>().Length;

        // Act
        var permissions = PermissionMapping.GetPermissions(Role.Admin);

        // Assert
        Assert.Equal(expectedScopeCount, permissions.Count);
        Assert.All(permissions, p => Assert.Equal(Role.Admin, p.Role));
    }

    /// <summary>
    /// GetPermissions 返回正确的权限列表。
    /// </summary>
    [Fact]
    public void GetPermissions_Viewer_ReturnsReadOnlyPermissions()
    {
        // Arrange
        var expectedScopeCount = Enum.GetValues<Scope>().Length;

        // Act
        var permissions = PermissionMapping.GetPermissions(Role.Viewer);

        // Assert
        Assert.Equal(expectedScopeCount, permissions.Count);
        Assert.All(permissions, p =>
        {
            Assert.Equal(Role.Viewer, p.Role);
            Assert.Single(p.AllowedOperations, Operation.Read);
        });
    }
}