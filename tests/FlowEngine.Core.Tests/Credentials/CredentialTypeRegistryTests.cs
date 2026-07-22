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
        Assert.Equal(["apiKey", "basicAuth", "database", "mongo", "oauth2", "redis", "s3", "smtp"], names);
    }

    [Fact]
    public void IsKnown_NewCredentialTypes_ReturnsTrue()
    {
        Assert.True(_registry.IsKnown("smtp"));
        Assert.True(_registry.IsKnown("s3"));
        Assert.True(_registry.IsKnown("redis"));
        Assert.True(_registry.IsKnown("mongo"));
    }

    [Fact]
    public void Get_Smtp_HasExpectedFieldsAndSecret()
    {
        var definition = _registry.Get("smtp");

        Assert.NotNull(definition);
        Assert.Equal("smtp", definition!.Name);
        Assert.Equal("SMTP", definition.DisplayName);
        Assert.Equal(["host", "port", "user", "password", "useSsl"], definition.Fields.Select(f => f.Name).ToList());
        Assert.True(definition.Fields.Single(f => f.Name == "host").IsRequired);
        Assert.False(definition.Fields.Single(f => f.Name == "port").IsRequired);
        Assert.False(definition.Fields.Single(f => f.Name == "user").IsRequired);
        Assert.True(definition.Fields.Single(f => f.Name == "password").IsRequired);
        Assert.True(definition.Fields.Single(f => f.Name == "password").Secret);
        Assert.False(definition.Fields.Single(f => f.Name == "useSsl").IsRequired);
    }

    [Fact]
    public void Get_S3_HasExpectedFieldsAndSecret()
    {
        var definition = _registry.Get("s3");

        Assert.NotNull(definition);
        Assert.Equal("s3", definition!.Name);
        Assert.Equal("S3 Compatible Object Storage", definition.DisplayName);
        Assert.Equal(["endpoint", "accessKey", "secretKey", "bucket", "region"], definition.Fields.Select(f => f.Name).ToList());
        Assert.True(definition.Fields.Single(f => f.Name == "endpoint").IsRequired);
        Assert.True(definition.Fields.Single(f => f.Name == "accessKey").IsRequired);
        Assert.True(definition.Fields.Single(f => f.Name == "secretKey").IsRequired);
        Assert.True(definition.Fields.Single(f => f.Name == "secretKey").Secret);
        Assert.True(definition.Fields.Single(f => f.Name == "bucket").IsRequired);
        Assert.False(definition.Fields.Single(f => f.Name == "region").IsRequired);
    }

    [Fact]
    public void Get_Redis_HasExpectedFieldsAndSecret()
    {
        var definition = _registry.Get("redis");

        Assert.NotNull(definition);
        Assert.Equal("redis", definition!.Name);
        Assert.Equal("Redis", definition.DisplayName);
        Assert.Equal(["host", "port", "password", "db"], definition.Fields.Select(f => f.Name).ToList());
        Assert.True(definition.Fields.Single(f => f.Name == "host").IsRequired);
        Assert.False(definition.Fields.Single(f => f.Name == "port").IsRequired);
        Assert.False(definition.Fields.Single(f => f.Name == "password").IsRequired);
        Assert.True(definition.Fields.Single(f => f.Name == "password").Secret);
        Assert.False(definition.Fields.Single(f => f.Name == "db").IsRequired);
    }

    [Fact]
    public void Get_Mongo_HasExpectedFieldsAndSecret()
    {
        var definition = _registry.Get("mongo");

        Assert.NotNull(definition);
        Assert.Equal("mongo", definition!.Name);
        Assert.Equal("MongoDB", definition.DisplayName);
        Assert.Equal(["connectionString", "database"], definition.Fields.Select(f => f.Name).ToList());
        Assert.True(definition.Fields.Single(f => f.Name == "connectionString").IsRequired);
        Assert.True(definition.Fields.Single(f => f.Name == "connectionString").Secret);
        Assert.True(definition.Fields.Single(f => f.Name == "database").IsRequired);
    }

    [Fact]
    public void Validate_SmtpWellFormed_ReturnsSuccess()
    {
        var fields = new Dictionary<string, string>
        {
            ["host"] = "smtp.example.com",
            ["password"] = "secret",
        };

        var result = _registry.Validate("smtp", fields);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_S3WellFormed_ReturnsSuccess()
    {
        var fields = new Dictionary<string, string>
        {
            ["endpoint"] = "https://s3.example.com",
            ["accessKey"] = "AKIA",
            ["secretKey"] = "secret",
            ["bucket"] = "my-bucket",
        };

        var result = _registry.Validate("s3", fields);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RedisWellFormed_ReturnsSuccess()
    {
        var fields = new Dictionary<string, string>
        {
            ["host"] = "localhost",
        };

        var result = _registry.Validate("redis", fields);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_MongoWellFormed_ReturnsSuccess()
    {
        var fields = new Dictionary<string, string>
        {
            ["connectionString"] = "mongodb://localhost:27017",
            ["database"] = "my-db",
        };

        var result = _registry.Validate("mongo", fields);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void IsKnown_KnownType_ReturnsTrue()
    {
        Assert.True(_registry.IsKnown("apiKey"));
        Assert.True(_registry.IsKnown("database"));
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
