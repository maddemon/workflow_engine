namespace FlowEngine.Core.Abstractions;

/// <summary>
/// 加密密钥提供者。
/// </summary>
public interface ICryptoKeyProvider
{
    /// <summary>
    /// 获取加密密钥。
    /// </summary>
    /// <returns>32 字节密钥。</returns>
    byte[] GetKey();
}
