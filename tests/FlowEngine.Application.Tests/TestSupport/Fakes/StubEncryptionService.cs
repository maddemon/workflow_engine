using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Entities;

namespace FlowEngine.Application.Tests.TestSupport.Fakes;

public sealed class StubEncryptionService : ICredentialEncryptionService
{
    public EncryptedField Encrypt(string plaintext, byte[] key)
    {
        return new EncryptedField
        {
            CipherText = $"encrypted:{plaintext}",
            Nonce = "nonce",
            Tag = "tag",
        };
    }

    public EncryptedField Encrypt(byte[] plaintext, byte[] key) =>
        new() { CipherText = Convert.ToBase64String(plaintext), Nonce = "nonce", Tag = "tag" };

    public string DecryptString(EncryptedField field, byte[] key) =>
        field.CipherText.Replace("encrypted:", "");

    public byte[] DecryptBytes(EncryptedField field, byte[] key) =>
        Convert.FromBase64String(field.CipherText);
}
