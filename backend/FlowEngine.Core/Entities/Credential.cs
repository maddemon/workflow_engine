using FlowEngine.Core.Attributes;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 凭据定义。
/// </summary>
public class Credential : Entity
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
    /// 加密字段数据映射。
    /// </summary>
    [JsonColumn]
    public Dictionary<string, EncryptedField> Data { get; set; } = [];

    /// <summary>
    /// 密钥版本。
    /// </summary>
    public string KeyVersion { get; set; } = string.Empty;
}
