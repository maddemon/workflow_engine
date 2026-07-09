namespace FlowEngine.Core.Credentials;

/// <summary>
/// 凭据类型定义。
/// </summary>
public sealed class CredentialTypeDefinition
{
    /// <summary>
    /// 类型名。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 字段定义列表。
    /// </summary>
    public IReadOnlyList<CredentialFieldDefinition> Fields { get; }

    public CredentialTypeDefinition(
        string name,
        string displayName,
        IEnumerable<CredentialFieldDefinition> fields)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Fields = fields?.ToList() ?? throw new ArgumentNullException(nameof(fields));
    }
}
