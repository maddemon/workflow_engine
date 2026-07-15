using System.Security.Cryptography;
using FlowEngine.Core.Entities;
using FlowEngine.Infrastructure.Security;

namespace FlowEngine.Runtime.Tests.Infrastructure;

/// <summary>
/// CredentialEncryptionService 真实 AesGcm 算法测试。
/// </summary>
public sealed class CredentialEncryptionServiceTests
{
    private static readonly byte[] ValidKey = Convert.FromHexString(
        "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F");

    private readonly CredentialEncryptionService _sut = new();

    [Fact]
    public void EncryptDecrypt_String_Roundtrip()
    {
        var plaintext = "my-secret-api-key-12345";
        var encrypted = _sut.Encrypt(plaintext, ValidKey);

        Assert.NotEqual(plaintext, encrypted.CipherText);
        Assert.False(encrypted.IsBinary);

        var decrypted = _sut.DecryptString(encrypted, ValidKey);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_Bytes_Roundtrip()
    {
        var plaintext = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE };
        var encrypted = _sut.Encrypt(plaintext, ValidKey);

        Assert.True(encrypted.IsBinary);

        var decrypted = _sut.DecryptBytes(encrypted, ValidKey);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_EmptyString_Roundtrip()
    {
        var plaintext = "";
        var encrypted = _sut.Encrypt(plaintext, ValidKey);

        // 空明文产生空密文
        Assert.Empty(Convert.FromHexString(encrypted.CipherText));

        var decrypted = _sut.DecryptString(encrypted, ValidKey);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_GeneratesDifferentNonceEachTime()
    {
        var plaintext = "same-input";
        var encrypted1 = _sut.Encrypt(plaintext, ValidKey);
        var encrypted2 = _sut.Encrypt(plaintext, ValidKey);

        // 不同 nonce 产生不同密文
        Assert.NotEqual(encrypted1.Nonce, encrypted2.Nonce);
        Assert.NotEqual(encrypted1.CipherText, encrypted2.CipherText);

        // 两者均可解密
        Assert.Equal(plaintext, _sut.DecryptString(encrypted1, ValidKey));
        Assert.Equal(plaintext, _sut.DecryptString(encrypted2, ValidKey));
    }

    [Fact]
    public void Decrypt_WrongTag_ThrowsCryptographicException()
    {
        var encrypted = _sut.Encrypt("secret", ValidKey);

        // 篡改 tag
        var tagBytes = Convert.FromHexString(encrypted.Tag);
        tagBytes[0] ^= 0xFF;
        encrypted.Tag = Convert.ToHexString(tagBytes).ToLowerInvariant();

        Assert.ThrowsAny<CryptographicException>(
            () => _sut.DecryptString(encrypted, ValidKey));
    }

    [Fact]
    public void Decrypt_WrongNonce_ThrowsCryptographicException()
    {
        var encrypted = _sut.Encrypt("secret", ValidKey);

        // 篡改 nonce
        var nonceBytes = Convert.FromHexString(encrypted.Nonce);
        nonceBytes[0] ^= 0xFF;
        encrypted.Nonce = Convert.ToHexString(nonceBytes).ToLowerInvariant();

        Assert.ThrowsAny<CryptographicException>(
            () => _sut.DecryptString(encrypted, ValidKey));
    }

    [Fact]
    public void Decrypt_WrongKey_ThrowsCryptographicException()
    {
        var encrypted = _sut.Encrypt("secret", ValidKey);

        var wrongKey = new byte[32];
        wrongKey[0] = 0xFF;

        Assert.ThrowsAny<CryptographicException>(
            () => _sut.DecryptString(encrypted, wrongKey));
    }

    [Fact]
    public void Encrypt_NullPlaintext_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => _sut.Encrypt((string)null!, ValidKey));
    }

    [Fact]
    public void Encrypt_NullBytes_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => _sut.Encrypt((byte[])null!, ValidKey));
    }

    [Fact]
    public void Decrypt_NullField_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => _sut.DecryptString(null!, ValidKey));
    }

    [Fact]
    public void Decrypt_NullFieldBytes_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => _sut.DecryptBytes(null!, ValidKey));
    }
}
