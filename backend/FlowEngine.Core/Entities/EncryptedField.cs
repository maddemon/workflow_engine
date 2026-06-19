namespace FlowEngine.Core.Entities;

/// <summary>
/// 加密字段数据。
/// </summary>
public class EncryptedField
{
    /// <summary>
    /// 密文。
    /// </summary>
    public string CipherText { get; set; } = string.Empty;

    /// <summary>
    /// 加密随机数。
    /// </summary>
    public string Nonce { get; set; } = string.Empty;

    /// <summary>
    /// 认证标签。
    /// </summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>
    /// 是否为二进制数据。
    /// </summary>
    public bool IsBinary { get; set; }
}
