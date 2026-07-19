using FlowEngine.Core.Credentials;

namespace FlowEngine.Core.Tests;

public class CredentialFieldDefinitionTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var def = new CredentialFieldDefinition("apiKey", "API Key", false, false, "Your API key");

        Assert.Equal("apiKey", def.Name);
        Assert.Equal("API Key", def.DisplayName);
        Assert.False(def.IsRequired);
        Assert.False(def.Secret);
        Assert.Equal("Your API key", def.Hint);
    }

    [Fact]
    public void Constructor_DefaultOptionalValues()
    {
        var def = new CredentialFieldDefinition("user", "Username");

        Assert.True(def.IsRequired);
        Assert.True(def.Secret);
        Assert.Null(def.Hint);
    }

    [Fact]
    public void Constructor_NullName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CredentialFieldDefinition(null!, "Name"));
    }

    [Fact]
    public void Constructor_NullDisplayName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CredentialFieldDefinition("name", null!));
    }
}
