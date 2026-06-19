using System.Security.Cryptography;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;

namespace FlowEngine.Infrastructure.Security;

/// <summary>
/// AES-256-GCM 凭据加密服务。
/// </summary>
public sealed class CredentialEncryptionService : ICredentialEncryptionService
{
    /// <summary>
    /// 加密明文字符串。
    /// </summary>
    /// <param name="plaintext">明文。</param>
    /// <param name="key">32 字节密钥。</param>
    /// <returns>加密字段。</returns>
    public EncryptedField Encrypt(string plaintext, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        return EncryptCore(plaintextBytes, key, isBinary: false);
    }

    /// <summary>
    /// 加密二进制数据。
    /// </summary>
    /// <param name="plaintext">明文字节数组。</param>
    /// <param name="key">32 字节密钥。</param>
    /// <returns>加密字段。</returns>
    public EncryptedField Encrypt(byte[] plaintext, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return EncryptCore(plaintext, key, isBinary: true);
    }

    /// <summary>
    /// 解密加密字段为字符串。
    /// </summary>
    /// <param name="field">加密字段。</param>
    /// <param name="key">32 字节密钥。</param>
    /// <returns>明文字符串。</returns>
    public string DecryptString(EncryptedField field, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(field);
        var plaintext = DecryptCore(field, key);
        return System.Text.Encoding.UTF8.GetString(plaintext);
    }

    /// <summary>
    /// 解密加密字段为字节数组。
    /// </summary>
    /// <param name="field">加密字段。</param>
    /// <param name="key">32 字节密钥。</param>
    /// <returns>明文字节数组。</returns>
    public byte[] DecryptBytes(EncryptedField field, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(field);
        return DecryptCore(field, key);
    }

    private static EncryptedField EncryptCore(byte[] plaintext, byte[] key, bool isBinary)
    {
        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12 bytes
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 bytes

        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return new EncryptedField
        {
            CipherText = Convert.ToHexString(ciphertext).ToLowerInvariant(),
            Nonce = Convert.ToHexString(nonce).ToLowerInvariant(),
            Tag = Convert.ToHexString(tag).ToLowerInvariant(),
            IsBinary = isBinary
        };
    }

    private static byte[] DecryptCore(EncryptedField field, byte[] key)
    {
        var ciphertext = Convert.FromHexString(field.CipherText);
        var nonce = Convert.FromHexString(field.Nonce);
        var tag = Convert.FromHexString(field.Tag);

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }
}
