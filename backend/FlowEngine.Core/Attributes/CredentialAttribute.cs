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
/// <remarks>
/// 标记凭据属性。
/// </remarks>
/// <param name="credentialType">凭据类型标识。</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CredentialAttribute(string credentialType) : Attribute
{
    /// <summary>
    /// 凭据类型标识（如 "apiKey"、"oauth"、"basicAuth"）。
    /// </summary>
    public string CredentialType { get; } = credentialType ?? throw new ArgumentNullException(nameof(credentialType));
}
