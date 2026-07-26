using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Scripting;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 聚合节点，将多个数据项合并为一个。
/// </summary>
[NodeMeta(TypeName = "aggregate", DisplayName = "Aggregate", Category = NodeCategory.Data, Icon = "layers", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class AggregateNode : NodeBase
{
    /// <summary>
    /// 聚合模式。
    /// </summary>
    [Description("How to aggregate items.")]
    public AggregateMode Mode { get; set; } = AggregateMode.Concatenate;

    /// <summary>
    /// 用于分组的字段名（GroupBy 模式下使用）。
    /// </summary>
    [Description("Field name to group by (for GroupBy mode).")]
    public string GroupByField { get; set; } = string.Empty;

    /// <summary>
    /// 输出字段名（Concatenate 模式下使用）。
    /// </summary>
    [Description("Field name to store aggregated items (for Concatenate mode).")]
    public string OutputFieldName { get; set; } = "items";

    /// <inheritdoc />
    public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        var inputBatch = input.InputBatch;

        var result = Mode switch
        {
            AggregateMode.Concatenate => AggregateConcatenate(inputBatch),
            AggregateMode.GroupBy => AggregateGroupBy(inputBatch),
            _ => throw new ArgumentOutOfRangeException(nameof(Mode), $"Unsupported aggregate mode: {Mode}")
        };

        return Task.FromResult(NodeHandlerOutput.Data(result));
    }

    private DataBatch AggregateConcatenate(DataBatch inputBatch)
    {
        var itemsArray = new JsonArray();
        foreach (var item in inputBatch.Items)
        {
            itemsArray.Add(item.Data?.DeepClone());
        }

        var outputObj = new JsonObject
        {
            [OutputFieldName] = itemsArray,
            ["count"] = inputBatch.Items.Count
        };

        return new DataBatch
        {
            Items =
            [
                new DataItem
                {
                    Data = outputObj,
                    Success = true,
                    SourceIndex = 0
                }
            ]
        };
    }

    private DataBatch AggregateGroupBy(DataBatch inputBatch)
    {
        if (string.IsNullOrEmpty(GroupByField))
        {
            return AggregateConcatenate(inputBatch);
        }

        var groups = new Dictionary<string, List<DataItem>>();

        foreach (var item in inputBatch.Items)
        {
            var keyValue = JsonPath.GetValue(item.Data, GroupByField) ?? string.Empty;
            if (!groups.ContainsKey(keyValue))
            {
                groups[keyValue] = new List<DataItem>();
            }
            groups[keyValue].Add(item);
        }

        var outputItems = new List<DataItem>();
        var index = 0;

        foreach (var (key, groupItems) in groups)
        {
            var itemsArray = new JsonArray();
            foreach (var item in groupItems)
            {
                itemsArray.Add(item.Data?.DeepClone());
            }

            var outputObj = new JsonObject
            {
                [GroupByField] = key,
                [OutputFieldName] = itemsArray,
                ["count"] = groupItems.Count
            };

            outputItems.Add(new DataItem
            {
                Data = outputObj,
                Success = true,
                SourceIndex = index++
            });
        }

        return new DataBatch { Items = outputItems };
    }

}

/// <summary>
/// 聚合模式。
/// </summary>
public enum AggregateMode
{
    /// <summary>连接所有项目到一个数组</summary>
    Concatenate,

    /// <summary>按字段分组</summary>
    GroupBy
}
