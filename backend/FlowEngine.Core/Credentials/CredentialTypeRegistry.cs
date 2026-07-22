namespace FlowEngine.Core.Credentials;

/// <summary>
/// 默认凭据类型注册表，内置常用凭据类型。
/// </summary>
public sealed class CredentialTypeRegistry
{
    private readonly Dictionary<string, CredentialTypeDefinition> _types;

    public CredentialTypeRegistry()
    {
        _types = CreateBuiltInTypes().ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<CredentialTypeDefinition> GetAll() => _types.Values.ToList();

    public CredentialTypeDefinition? Get(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        return _types.GetValueOrDefault(type);
    }

    public bool IsKnown(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        return _types.ContainsKey(type);
    }

    public ValidationResult Validate(string type, Dictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var definition = Get(type);
        if (definition is null)
        {
            var knownTypes = string.Join(", ", _types.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
            return ValidationResult.Failure($"未知凭据类型 '{type}'。可用类型：{knownTypes}");
        }

        var missingFields = definition.Fields
            .Where(f => f.IsRequired && !fields.ContainsKey(f.Name))
            .Select(f => f.Name)
            .ToList();

        if (missingFields.Count > 0)
        {
            var missing = string.Join(", ", missingFields);
            return ValidationResult.Failure($"凭据类型 '{type}' 缺少必填字段：{missing}");
        }

        if (string.Equals(type, "oauth2", StringComparison.OrdinalIgnoreCase) &&
            fields.TryGetValue("provider", out var provider) &&
            !string.IsNullOrWhiteSpace(provider) &&
            !IsKnownOAuth2Provider(provider))
        {
            return ValidationResult.Failure($"凭据类型 'oauth2' 的 provider 值 '{provider}' 无效。可用值：standard, dingtalk");
        }

        return ValidationResult.Success();
    }

    private static bool IsKnownOAuth2Provider(string provider)
    {
        return string.Equals(provider, "standard", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(provider, "dingtalk", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<CredentialTypeDefinition> CreateBuiltInTypes()
    {
        yield return new CredentialTypeDefinition(
            "apiKey",
            "API Key",
            [
                new CredentialFieldDefinition("apiKey", "API Key", isRequired: true, secret: true),
            ]);

        yield return new CredentialTypeDefinition(
            "database",
            "Database",
            [
                new CredentialFieldDefinition("dbType", "Database Type", isRequired: true, secret: false, hint: "postgresql | mysql | sqlserver | sqlite"),
                new CredentialFieldDefinition("host", "Host", isRequired: false, secret: false),
                new CredentialFieldDefinition("port", "Port", isRequired: false, secret: false),
                new CredentialFieldDefinition("database", "Database", isRequired: false, secret: false),
                new CredentialFieldDefinition("userid", "User ID", isRequired: false, secret: false),
                new CredentialFieldDefinition("password", "Password", isRequired: false, secret: true),
                new CredentialFieldDefinition("ssl", "SSL Mode", isRequired: false, secret: false, hint: "依方言而定，如 require / disable / true / false"),
                new CredentialFieldDefinition("dataSource", "Data Source (SQLite)", isRequired: false, secret: false, hint: "SQLite 专用，如 /path/db.sqlite 或 :memory:"),
                new CredentialFieldDefinition("mode", "Mode (SQLite)", isRequired: false, secret: false, hint: "SQLite 专用，如 Memory"),
                new CredentialFieldDefinition("cache", "Cache (SQLite)", isRequired: false, secret: false, hint: "SQLite 专用，如 Shared"),
            ]);

        yield return new CredentialTypeDefinition(
            "basicAuth",
            "Basic Auth",
            [
                new CredentialFieldDefinition("username", "Username", isRequired: true, secret: false),
                new CredentialFieldDefinition("password", "Password", isRequired: true, secret: true),
            ]);

        yield return new CredentialTypeDefinition(
            "oauth2",
            "OAuth2",
            [
                new CredentialFieldDefinition("tokenUrl", "Token URL", isRequired: true, secret: false, hint: "OAuth2 token 端点地址"),
                new CredentialFieldDefinition("clientId", "Client ID", isRequired: true, secret: false),
                new CredentialFieldDefinition("clientSecret", "Client Secret", isRequired: true, secret: true),
                new CredentialFieldDefinition("scope", "Scope", isRequired: false, secret: false),
                new CredentialFieldDefinition("grant", "Grant Type", isRequired: false, secret: false, hint: "默认 client_credentials"),
                new CredentialFieldDefinition("tokenPath", "Token Path", isRequired: false, secret: false, hint: "默认 access_token"),
                new CredentialFieldDefinition("provider", "Provider", isRequired: false, secret: false, hint: "standard | dingtalk（决定取 token 形态，默认 standard）"),
            ]);

        yield return new CredentialTypeDefinition(
            "smtp",
            "SMTP",
            [
                new CredentialFieldDefinition("host", "Host", isRequired: true, secret: false),
                new CredentialFieldDefinition("port", "Port", isRequired: false, secret: false),
                new CredentialFieldDefinition("user", "User", isRequired: false, secret: false),
                new CredentialFieldDefinition("password", "Password", isRequired: true, secret: true),
                new CredentialFieldDefinition("useSsl", "Use SSL", isRequired: false, secret: false),
            ]);

        yield return new CredentialTypeDefinition(
            "s3",
            "S3 Compatible Object Storage",
            [
                new CredentialFieldDefinition("endpoint", "Endpoint", isRequired: true, secret: false),
                new CredentialFieldDefinition("accessKey", "Access Key", isRequired: true, secret: false),
                new CredentialFieldDefinition("secretKey", "Secret Key", isRequired: true, secret: true),
                new CredentialFieldDefinition("bucket", "Bucket", isRequired: true, secret: false),
                new CredentialFieldDefinition("region", "Region", isRequired: false, secret: false),
            ]);

        yield return new CredentialTypeDefinition(
            "redis",
            "Redis",
            [
                new CredentialFieldDefinition("host", "Host", isRequired: true, secret: false),
                new CredentialFieldDefinition("port", "Port", isRequired: false, secret: false),
                new CredentialFieldDefinition("password", "Password", isRequired: false, secret: true),
                new CredentialFieldDefinition("db", "DB", isRequired: false, secret: false),
            ]);

        yield return new CredentialTypeDefinition(
            "mongo",
            "MongoDB",
            [
                new CredentialFieldDefinition("connectionString", "Connection String", isRequired: true, secret: true),
                new CredentialFieldDefinition("database", "Database", isRequired: true, secret: false),
            ]);
    }
}
