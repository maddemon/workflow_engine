namespace FlowEngine.Core.Credentials;

/// <summary>
/// 默认凭据类型注册表，内置常用凭据类型。
/// </summary>
public sealed class CredentialTypeRegistry : ICredentialTypeRegistry
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

        return ValidationResult.Success();
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
            "connectionString",
            "Connection String",
            [
                new CredentialFieldDefinition("connectionString", "Connection String", isRequired: true, secret: true),
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
    }
}
