using FlowEngine.Core.Credentials;

namespace FlowEngine.Core.Tests.Credentials;

public sealed class CredentialTypeRegistryTests
{
    private readonly CredentialTypeRegistry _registry = new();

    [Fact]
    public void Get_Oauth2_ReturnsCorrectFields()
    {
        var definition = _registry.Get("oauth2");

        Assert.NotNull(definition);
        Assert.Equal("oauth2", definition!.Name);
        Assert.Equal("OAuth2", definition.DisplayName);
        var fieldNames = definition.Fields.Select(f => f.Name).ToList();
        Assert.Equal(["tokenUrl", "clientId", "clientSecret", "scope", "grant", "tokenPath", "provider"], fieldNames);
    }

    [Fact]
    public void Get_Oauth2_HasOptionalProviderField()
    {
        var definition = _registry.Get("oauth2");

        Assert.NotNull(definition);
        var provider = definition!.Fields.SingleOrDefault(f => f.Name == "provider");
        Assert.NotNull(provider);
        Assert.False(provider!.IsRequired);
    }

    [Fact]
    public void Validate_Oauth2WithProviderField_ReturnsSuccess()
    {
        var fields = new Dictionary<string, string>
        {
            ["tokenUrl"] = "https://oapi.dingtalk.com/gettoken",
            ["clientId"] = "appkey",
            ["clientSecret"] = "appsecret",
            ["provider"] = "dingtalk"
        };

        var result = _registry.Validate("oauth2", fields);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Oauth2WithRequiredFields_ReturnsSuccess()
    {
        var fields = new Dictionary<string, string>
        {
            ["tokenUrl"] = "https://example.com/token",
            ["clientId"] = "client-id",
            ["clientSecret"] = "client-secret",
        };

        var result = _registry.Validate("oauth2", fields);

        Assert.True(result.IsValid);
        Assert.Empty(result.ErrorMessage);
    }

    [Fact]
    public void Validate_Oauth2MissingClientSecret_ReturnsFailureWithMissingField()
    {
        var fields = new Dictionary<string, string>
        {
            ["tokenUrl"] = "https://example.com/token",
            ["clientId"] = "client-id",
        };

        var result = _registry.Validate("oauth2", fields);

        Assert.False(result.IsValid);
        Assert.Contains("clientSecret", result.ErrorMessage);
    }

    [Fact]
    public void Validate_Oauth2WithInvalidProvider_ReturnsFailure()
    {
        var fields = new Dictionary<string, string>
        {
            ["tokenUrl"] = "https://example.com/token",
            ["clientId"] = "client-id",
            ["clientSecret"] = "client-secret",
            ["provider"] = "unknown"
        };

        var result = _registry.Validate("oauth2", fields);

        Assert.False(result.IsValid);
        Assert.Contains("provider", result.ErrorMessage);
        Assert.Contains("unknown", result.ErrorMessage);
    }

    [Fact]
    public void Validate_UnknownType_ReturnsFailure()
    {
        var result = _registry.Validate("unknown", []);

        Assert.False(result.IsValid);
        Assert.Contains("未知凭据类型", result.ErrorMessage);
    }

    [Fact]
    public void GetAll_ReturnsBuiltInTypes()
    {
        var types = _registry.GetAll();

        var names = types.Select(t => t.Name).OrderBy(n => n).ToList();
        Assert.Equal(["apiKey", "basicAuth", "connectionString", "oauth2"], names);
    }

    [Fact]
    public void IsKnown_KnownType_ReturnsTrue()
    {
        Assert.True(_registry.IsKnown("apiKey"));
        Assert.True(_registry.IsKnown("connectionString"));
        Assert.True(_registry.IsKnown("basicAuth"));
        Assert.True(_registry.IsKnown("oauth2"));
    }

    [Fact]
    public void IsKnown_UnknownType_ReturnsFalse()
    {
        Assert.False(_registry.IsKnown("unknown"));
        Assert.False(_registry.IsKnown(string.Empty));
    }

    [Theory]
    [InlineData("apiKey", "apiKey")]
    [InlineData("APIKEY", "apiKey")]
    [InlineData("OAuth2", "oauth2")]
    public void Get_IsCaseInsensitive(string input, string expectedName)
    {
        var definition = _registry.Get(input);

        Assert.NotNull(definition);
        Assert.Equal(expectedName, definition!.Name);
    }
}
