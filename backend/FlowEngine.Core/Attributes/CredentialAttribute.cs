namespace FlowEngine.Core.Attributes;

/// <summary>
/// 标记属性为凭据类型，指定凭据分类。
/// 配合 <c>CredentialValue?</c> 属性使用。
/// </summary>
/// <example>
/// <code>
/// [Credential("apiKey")]
/// public CredentialValue? ApiCredential { get; set; }
///
/// [Credential("apiKey", "oauth2")]
/// public CredentialValue? AuthCredential { get; set; }
/// </code>
/// </example>
/// <remarks>
/// 标记凭据属性。支持单个或多个凭据类型。
/// </remarks>
/// <param name="credentialType">至少一个凭据类型标识（如 "apiKey"、"oauth2"、"basicAuth"）。</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CredentialAttribute(params string[] credentialType) : Attribute
{
    /// <summary>
    /// 允许的凭据类型列表。
    /// </summary>
    public string[] CredentialTypes { get; } = credentialType.Length > 0
        ? credentialType
        : throw new ArgumentException("至少需要指定一个凭据类型", nameof(credentialType));

    /// <summary>
    /// 主凭据类型（向后兼容）。
    /// </summary>
    public string CredentialType => CredentialTypes[0];
}
