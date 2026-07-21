using System.Security.Cryptography;
using FlowEngine.Core.Abstractions;

namespace FlowEngine.Application.Tests.TestSupport.Fakes;

/// <summary>
/// 测试用版本化密钥提供者：内置 v1 / v2 两把互异密钥，用于验证按 <c>KeyVersion</c> 解析密钥的正确性。
/// </summary>
public sealed class VersionedTestKeyProvider : ICryptoKeyProvider
{
    public string CurrentVersion => "v1";

    private readonly byte[] _v1 = new byte[32];
    private readonly byte[] _v2 = new byte[32];

    public VersionedTestKeyProvider()
    {
        // 用可区分的内容填充，确保两把密钥不同，从而暴露版本错配。
        for (var i = 0; i < 32; i++)
        {
            _v1[i] = (byte)(i + 1);
            _v2[i] = (byte)(100 + i);
        }
    }

    public byte[] GetKey() => _v1.ToArray();

    public byte[] GetKey(string keyVersion)
    {
        if (string.IsNullOrEmpty(keyVersion) || string.Equals(keyVersion, "v1", StringComparison.OrdinalIgnoreCase))
        {
            return _v1.ToArray();
        }

        if (string.Equals(keyVersion, "v2", StringComparison.OrdinalIgnoreCase))
        {
            return _v2.ToArray();
        }

        throw new CryptographicException($"未知密钥版本 {keyVersion}");
    }
}
