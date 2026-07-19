using FlowEngine.Core.Authorization;
using FlowEngine.Core.Enums;

namespace FlowEngine.Core.Tests;

public class AuthorizePermissionAttributeTests
{
    [Fact]
    public void Constructor_SetsScopeAndOperation()
    {
        var attr = new AuthorizePermissionAttribute(Scope.Workflow, Operation.Execute);

        Assert.Equal(Scope.Workflow, attr.Scope);
        Assert.Equal(Operation.Execute, attr.Operation);
    }

    [Fact]
    public void AttributeUsage_AllowsMethodAndClass()
    {
        var usage = typeof(AuthorizePermissionAttribute).GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>().First();

        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Method));
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Class));
        Assert.False(usage.AllowMultiple);
    }
}
