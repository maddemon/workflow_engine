namespace FlowEngine.Core.Entities;

/// <summary>
/// 数据批次，包含一组数据项。
/// </summary>
public class DataBatch
{
    /// <summary>
    /// 数据项列表。
    /// </summary>
    public List<DataItem> Items { get; set; } = [];
}
