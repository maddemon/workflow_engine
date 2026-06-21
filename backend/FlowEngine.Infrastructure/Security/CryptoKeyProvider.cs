using FlowEngine.Core.Abstractions;

namespace FlowEngine.Infrastructure.Security;

/// <summary>
/// 加密密钥提供者。
/// 优先从环境变量读取（生产环境），否则从本地文件读取或自动生成。
/// 密钥首次调用 <see cref="GetKey"/> 时延迟加载/生成，构造函数无 I/O 副作用。
/// </summary>
public sealed class CryptoKeyProvider : ICryptoKeyProvider
{
    private readonly Lazy<byte[]> _key;
    private readonly string _keyFilePath;

    /// <summary>
    /// 初始化密钥提供者。不含 I/O 操作。
    /// </summary>
    /// <param name="keyFilePath">密钥文件路径，默认为 data/crypto.key。</param>
    public CryptoKeyProvider(string? keyFilePath = null)
    {
        _keyFilePath = keyFilePath ?? Path.Combine("data", "crypto.key");
        _key = new Lazy<byte[]>(LoadKey, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private byte[] LoadKey()
    {
        var envKey = Environment.GetEnvironmentVariable("FLOWENGINE_CRYPTO_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            return ParseHexKey(envKey, "环境变量 FLOWENGINE_CRYPTO_KEY");
        }

        return LoadOrGenerateKey(_keyFilePath);
    }

    private static byte[] ParseHexKey(string hexKey, string source)
    {
        try
        {
            var key = Convert.FromHexString(hexKey);
            if (key.Length != 32)
            {
                throw new InvalidOperationException(
                    $"{source} 长度无效：期望 32 字节（64 位十六进制字符），实际 {key.Length} 字节。");
            }
            return key;
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                $"{source} 格式无效。请设置 64 位十六进制字符串（32 字节 AES-256 密钥）。");
        }
    }

    private static byte[] LoadOrGenerateKey(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(filePath))
        {
            var hexKey = File.ReadAllText(filePath).Trim();
            return ParseHexKey(hexKey, $"密钥文件 {filePath}");
        }

        var newKey = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(newKey);

        var hex = Convert.ToHexString(newKey);
        File.WriteAllText(filePath, hex);

        return newKey;
    }

    /// <summary>
    /// 获取加密密钥。首次调用时延迟加载/生成密钥。
    /// </summary>
    /// <returns>32 字节密钥的防御性副本。</returns>
    public byte[] GetKey() => _key.Value.ToArray();
}