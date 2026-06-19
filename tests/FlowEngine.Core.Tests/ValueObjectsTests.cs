using FlowEngine.Core.ValueObjects;

namespace FlowEngine.Core.Tests;

public class ValueObjectsTests
{
    [Fact]
    public void ExecutionId_With_Same_Value_Are_Equal()
    {
        var guid = Guid.NewGuid();
        var a = ExecutionId.From(guid);
        var b = ExecutionId.From(guid);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ExecutionId_With_Different_Value_Are_Not_Equal()
    {
        var a = ExecutionId.New();
        var b = ExecutionId.New();

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void NodeDefinitionId_With_Same_Value_Are_Equal()
    {
        var guid = Guid.NewGuid();
        var a = NodeDefinitionId.From(guid);
        var b = NodeDefinitionId.From(guid);

        Assert.Equal(a, b);
    }

    [Fact]
    public void WorkflowDefinitionId_With_Same_Value_Are_Equal()
    {
        var guid = Guid.NewGuid();
        var a = WorkflowDefinitionId.From(guid);
        var b = WorkflowDefinitionId.From(guid);

        Assert.Equal(a, b);
    }

    [Fact]
    public void CredentialKey_With_Same_Fields_Are_Equal()
    {
        var credentialId = Guid.NewGuid();
        var a = new CredentialKey(credentialId, "apiKey");
        var b = new CredentialKey(credentialId, "apiKey");

        Assert.Equal(a, b);
    }

    [Fact]
    public void CredentialKey_With_Different_Fields_Are_Not_Equal()
    {
        var a = new CredentialKey(Guid.NewGuid(), "apiKey");
        var b = new CredentialKey(Guid.NewGuid(), "secret");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ExecutionId_Is_Immutable_Via_With_Expression()
    {
        var original = ExecutionId.New();
        var copy = original with { Value = Guid.NewGuid() };

        Assert.NotEqual(original.Value, copy.Value);
        Assert.NotEqual(original, copy);
    }

    [Fact]
    public void CredentialKey_Is_Immutable_Via_With_Expression()
    {
        var original = new CredentialKey(Guid.NewGuid(), "apiKey");
        var copy = original with { FieldName = "secret" };

        Assert.Equal("apiKey", original.FieldName);
        Assert.Equal("secret", copy.FieldName);
        Assert.NotEqual(original, copy);
    }
}
