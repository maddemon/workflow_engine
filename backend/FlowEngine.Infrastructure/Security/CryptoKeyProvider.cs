using System.Security.Cryptography;
using FlowEngine.Core.Abstractions;

namespace FlowEngine.Infrastructure.Security;

/// <summary>
/// 加密密钥提供者。
/// 优先从环境变量读取（生产环境），否则从本地文件读取或自动生成。
/// 密钥首次调用 <see cref="GetKey"/> 时延迟加载/生成，构造函数无 I/O 副作用。
/// </summary>
/// <remarks>
/// 支持按版本解析密钥：<see cref="GetKey(string)"/> 根据凭据存储的 <see cref="Credential.KeyVersion"/>
/// 返回对应密钥；空/空串/当前版本回退到当前密钥（兼容未带版本的遗留数据）。
/// 当前仅注册 v1 一把密钥，多版本密钥轮换时再向 <c>_versionedKeys</c> 登记。
/// </remarks>
public sealed class CryptoKeyProvider : ICryptoKeyProvider
{
    private const string CurrentKeyVersion = "v1";

    private readonly Lazy<byte[]> _key;
    private readonly string _keyFilePath;
    private readonly Dictionary<string, byte[]> _versionedKeys = new(StringComparer.OrdinalIgnoreCase);

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

        if (IsNonDevelopmentEnvironment())
        {
            throw new InvalidOperationException(
                "生产环境必须设置环境变量 FLOWENGINE_CRYPTO_KEY（64 位十六进制字符串，32 字节 AES-256 密钥）。" +
                "禁止在文件系统中自动生成并明文保存加密密钥。");
        }

        return LoadOrGenerateKey(_keyFilePath);
    }

    private static bool IsNonDevelopmentEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Production";
        return !string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
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
    /// 当前密钥版本号。
    /// </summary>
    public string CurrentVersion => CurrentKeyVersion;

    /// <summary>
    /// 获取加密密钥。首次调用时延迟加载/生成密钥。
    /// </summary>
    /// <returns>32 字节密钥的防御性副本。</returns>
    public byte[] GetKey() => _key.Value.ToArray();

    /// <summary>
    /// 获取指定版本的密钥。空/空串/当前版本回退到当前密钥（兼容遗留数据）；
    /// 未知版本抛出 <see cref="CryptographicException"/>。
    /// </summary>
    /// <param name="keyVersion">密钥版本号。</param>
    /// <returns>32 字节密钥的防御性副本。</returns>
    /// <exception cref="CryptographicException">当指定版本不存在对应密钥时抛出。</exception>
    public byte[] GetKey(string keyVersion)
    {
        if (string.IsNullOrEmpty(keyVersion)
            || string.Equals(keyVersion, CurrentKeyVersion, StringComparison.OrdinalIgnoreCase))
        {
            return _key.Value.ToArray();
        }

        // 延迟将当前密钥登记到版本字典，确保 v1 始终可解析且密钥来源唯一。
        if (!_versionedKeys.ContainsKey(CurrentKeyVersion))
        {
            _versionedKeys[CurrentKeyVersion] = _key.Value;
        }

        if (_versionedKeys.TryGetValue(keyVersion, out var key))
        {
            return key.ToArray();
        }

        throw new CryptographicException(
            $"未找到密钥版本 '{keyVersion}' 对应的密钥。当前版本为 {CurrentKeyVersion}，" +
            "仅支持已注册的密钥版本。");
    }
}