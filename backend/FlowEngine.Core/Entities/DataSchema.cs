namespace FlowEngine.Core.Entities;

/// <summary>
/// 数据模式。
/// </summary>
public class DataSchema
{
    /// <summary>
    /// 数据类型。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 对象属性模式映射。
    /// </summary>
    public Dictionary<string, DataSchema> Properties { get; set; } = [];

    /// <summary>
    /// 必填属性列表。
    /// </summary>
    public List<string> Required { get; set; } = [];

    /// <summary>
    /// 数组项模式。
    /// </summary>
    public DataSchema? Items { get; set; }

    /// <summary>
    /// 描述。
    /// </summary>
    public string? Description { get; set; }
}
