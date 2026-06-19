namespace FlowEngine.Core.ValueObjects;

/// <summary>
/// 凭据字段键。
/// </summary>
/// <param name="CredentialId">凭据 ID。</param>
/// <param name="FieldName">字段名称。</param>
public readonly record struct CredentialKey(Guid CredentialId, string FieldName)
{
    /// <summary>
    /// 返回键的字符串表示。
    /// </summary>
    /// <returns>字符串表示。</returns>
    public override string ToString() => $"{CredentialId}:{FieldName}";
}
