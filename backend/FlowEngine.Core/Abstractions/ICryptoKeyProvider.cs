using System.Security.Cryptography;

namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 加密密钥提供者。
/// </summary>
public interface ICryptoKeyProvider
{
    /// <summary>
    /// 当前密钥版本号。加密时使用当前版本；解密时按凭据存储的 <c>KeyVersion</c> 解析对应密钥。
    /// </summary>
    string CurrentVersion { get; }

    /// <summary>
    /// 获取加密用密钥（当前版本）。
    /// </summary>
    /// <returns>32 字节密钥的防御性副本。</returns>
    byte[] GetKey();

    /// <summary>
    /// 获取指定版本的密钥。
    /// </summary>
    /// <param name="keyVersion">
    /// 密钥版本号；为空、空字符串或与 <see cref="CurrentVersion"/> 相同时返回当前密钥
    /// （兼容未带版本的遗留数据）。
    /// </param>
    /// <returns>32 字节密钥的防御性副本。</returns>
    /// <exception cref="CryptographicException">当指定版本不存在对应密钥时抛出。</exception>
    byte[] GetKey(string keyVersion);
}
