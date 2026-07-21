using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FlowEngine.Core.Entities;

/// <summary>
/// 数据批次，包含一组数据项。
/// </summary>
[NotMapped]
public class DataBatch
{
    /// <summary>
    /// 数据项列表。
    /// </summary>
    public List<DataItem> Items { get; set; } = [];

    /// <summary>
    /// 合并两个数据批次为一个新批次。两批次的数据项被复制并按顺序重新索引
    /// <see cref="DataItem.SourceIndex"/>：第一个批次的项在前（从 0 起算），
    /// 第二个批次的项在后（从第一个批次的项数起算）。不会修改入参。
    /// </summary>
    public static DataBatch Merge(DataBatch a, DataBatch b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var merged = new DataBatch();
        for (var i = 0; i < a.Items.Count; i++)
        {
            var item = a.Items[i];
            merged.Items.Add(new DataItem
            {
                Data = item.Data,
                Success = item.Success,
                Error = item.Error,
                SourceIndex = i,
                AttachmentId = item.AttachmentId
            });
        }

        for (var i = 0; i < b.Items.Count; i++)
        {
            var item = b.Items[i];
            merged.Items.Add(new DataItem
            {
                Data = item.Data,
                Success = item.Success,
                Error = item.Error,
                SourceIndex = a.Items.Count + i,
                AttachmentId = item.AttachmentId
            });
        }

        return merged;
    }
}
