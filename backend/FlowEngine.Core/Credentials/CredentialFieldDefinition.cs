namespace FlowEngine.Core.Credentials;

/// <summary>
/// 凭据字段定义。
/// </summary>
public sealed class CredentialFieldDefinition
{
    /// <summary>
    /// 字段名。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 是否必填。
    /// </summary>
    public bool IsRequired { get; }

    /// <summary>
    /// 是否敏感字段。
    /// </summary>
    public bool Secret { get; }

    /// <summary>
    /// 提示文本。
    /// </summary>
    public string? Hint { get; }

    public CredentialFieldDefinition(
        string name,
        string displayName,
        bool isRequired = true,
        bool secret = true,
        string? hint = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        IsRequired = isRequired;
        Secret = secret;
        Hint = hint;
    }
}
