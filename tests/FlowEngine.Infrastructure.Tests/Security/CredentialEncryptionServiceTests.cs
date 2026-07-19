using System.Text;
using FlowEngine.Core.Abstractions;
using FlowEngine.Infrastructure.Security;
using Xunit;

namespace FlowEngine.Infrastructure.Tests.Security;

public sealed class CredentialEncryptionServiceTests
{
    private readonly ICredentialEncryptionService _service = new CredentialEncryptionService();
    private readonly byte[] _key = Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef");

    [Fact]
    public void EncryptString_AndDecryptString_Roundtrip()
    {
        const string plaintext = "sensitive value";

        var encrypted = _service.Encrypt(plaintext, _key);
        var decrypted = _service.DecryptString(encrypted, _key);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void EncryptBytes_AndDecryptBytes_Roundtrip()
    {
        var plaintext = Encoding.UTF8.GetBytes("binary secret");

        var encrypted = _service.Encrypt(plaintext, _key);
        var decrypted = _service.DecryptBytes(encrypted, _key);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void EncryptString_Twice_ProducesDifferentCiphertexts()
    {
        const string plaintext = "same text";

        var encrypted1 = _service.Encrypt(plaintext, _key);
        var encrypted2 = _service.Encrypt(plaintext, _key);

        Assert.NotEqual(encrypted1.CipherText, encrypted2.CipherText);
        Assert.NotEqual(encrypted1.Nonce, encrypted2.Nonce);
    }

    [Fact]
    public void Encrypt_NullPlaintextString_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Encrypt((string)null!, _key));
    }

    [Fact]
    public void Encrypt_NullPlaintextBytes_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Encrypt((byte[])null!, _key));
    }

    [Fact]
    public void DecryptString_WithWrongKey_ThrowsCryptographicException()
    {
        var encrypted = _service.Encrypt("plain", _key);
        var wrongKey = Encoding.UTF8.GetBytes("wrongkeywrongkeywrongkeywrongkey");

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() =>
            _service.DecryptString(encrypted, wrongKey));
    }
}
