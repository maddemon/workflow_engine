namespace FlowEngine.Core.Attributes;

/// <summary>
/// 标记属性为凭据类型，指定凭据分类。
/// 配合 <c>CredentialValue?</c> 属性使用。
/// </summary>
/// <example>
/// <code>
/// [Credential("apiKey")]
/// public CredentialValue? ApiCredential { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CredentialAttribute : Attribute
{
    /// <summary>
    /// 凭据类型标识（如 "apiKey"、"oauth"、"basicAuth"）。
    /// </summary>
    public string CredentialType { get; }

    /// <summary>
    /// 标记凭据属性。
    /// </summary>
    /// <param name="credentialType">凭据类型标识。</param>
    public CredentialAttribute(string credentialType)
    {
        CredentialType = credentialType ?? throw new ArgumentNullException(nameof(credentialType));
    }
}
