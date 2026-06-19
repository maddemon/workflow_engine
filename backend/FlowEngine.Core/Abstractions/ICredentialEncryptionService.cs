using FlowEngine.Core.Entities;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 凭据加密服务。
/// </summary>
public interface ICredentialEncryptionService
{
    /// <summary>
    /// 加密明文字符串。
    /// </summary>
    /// <param name="plaintext">明文。</param>
    /// <param name="key">32 字节密钥。</param>
    /// <returns>加密字段。</returns>
    EncryptedField Encrypt(string plaintext, byte[] key);

    /// <summary>
    /// 加密二进制数据。
    /// </summary>
    /// <param name="plaintext">明文字节数组。</param>
    /// <param name="key">32 字节密钥。</param>
    /// <returns>加密字段。</returns>
    EncryptedField Encrypt(byte[] plaintext, byte[] key);

    /// <summary>
    /// 解密加密字段为字符串。
    /// </summary>
    /// <param name="field">加密字段。</param>
    /// <param name="key">32 字节密钥。</param>
    /// <returns>明文字符串。</returns>
    string DecryptString(EncryptedField field, byte[] key);

    /// <summary>
    /// 解密加密字段为字节数组。
    /// </summary>
    /// <param name="field">加密字段。</param>
    /// <param name="key">32 字节密钥。</param>
    /// <returns>明文字节数组。</returns>
    byte[] DecryptBytes(EncryptedField field, byte[] key);
}
