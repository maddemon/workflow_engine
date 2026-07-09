namespace FlowEngine.Core.Credentials;

/// <summary>
/// 凭据类型注册表。
/// </summary>
public interface ICredentialTypeRegistry
{
    /// <summary>
    /// 获取所有已注册的凭据类型定义。
    /// </summary>
    IReadOnlyCollection<CredentialTypeDefinition> GetAll();

    /// <summary>
    /// 按名称获取凭据类型定义，未知类型返回 null。
    /// </summary>
    CredentialTypeDefinition? Get(string type);

    /// <summary>
    /// 判断指定类型是否已知。
    /// </summary>
    bool IsKnown(string type);

    /// <summary>
    /// 校验指定类型的字段是否满足 schema。
    /// </summary>
    ValidationResult Validate(string type, Dictionary<string, string> fields);
}
