namespace FlowEngine.Core.Entities;

/// <summary>
/// 凭据值。
/// </summary>
public class CredentialValue
{
    /// <summary>
    /// 凭据名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 凭据类型。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 明文字段映射。
    /// </summary>
    public Dictionary<string, string> Fields { get; set; } = [];

    /// <summary>
    /// 二进制字段映射。
    /// </summary>
    public Dictionary<string, byte[]> BinaryFields { get; set; } = [];
}
