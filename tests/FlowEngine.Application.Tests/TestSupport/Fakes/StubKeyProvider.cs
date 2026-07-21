using System.Security.Cryptography;
using FlowEngine.Core.Abstractions;

namespace FlowEngine.Application.Tests.TestSupport.Fakes;

public sealed class StubKeyProvider : ICryptoKeyProvider
{
    public string CurrentVersion => "v1";

    public byte[] GetKey() => new byte[32];

    public byte[] GetKey(string keyVersion) =>
        string.IsNullOrEmpty(keyVersion) || string.Equals(keyVersion, "v1", StringComparison.OrdinalIgnoreCase)
            ? new byte[32]
            : throw new CryptographicException($"未知密钥版本 {keyVersion}");
}
