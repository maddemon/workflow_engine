using System.ComponentModel;
using System.Text.Json.Nodes;
using FlowEngine.Core;
using FlowEngine.Core.Abstractions;
using FlowEngine.Core.Attributes;
using FlowEngine.Core.Entities;
using FlowEngine.Core.Enums;
using FlowEngine.Core.Exceptions;
using FlowEngine.Core.Scripting;

namespace FlowEngine.Plugins.Standard;

/// <summary>
/// 列表操作节点，对数据批次做聚合 / 拆分 / 合并 / 分组等列表级变换。
/// 复用 <see cref="JsonPath"/> 读取字段，避免散落路径解析逻辑。
/// </summary>
[NodeMeta(TypeName = "listOps", DisplayName = "List Operations", Category = NodeCategory.Data, Icon = "list", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class ListOpsNode : NodeBase
{
    /// <summary>
    /// 列表操作类型。
    /// </summary>
    [Description("List operation to perform: summarize | fieldToItems | itemsToField | groupBy.")]
    public ListOpsOperation Operation { get; set; } = ListOpsOperation.Summarize;

    /// <summary>
    /// 参与运算的字段名（summarize / fieldToItems / itemsToField 必填；groupBy 在聚合非 count 时必填）。
    /// </summary>
    [Description("Field to operate on. Required for summarize/fieldToItems/itemsToField; required for groupBy when Aggregate is not count.")]
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// 聚合函数（summarize / groupBy 使用）。
    /// </summary>
    [Description("Aggregation function for summarize/groupBy: sum | count | avg | min | max.")]
    public ListOpsAggregate Aggregate { get; set; } = ListOpsAggregate.Sum;

    /// <summary>
    /// 分组字段名（groupBy 必填）。
    /// </summary>
    [Description("Group-by field name (required for groupBy).")]
    public string GroupBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        var inputBatch = input.InputBatch;

        // 空输入 -> 空批次（成功，但无输出项）。
        if (inputBatch.Items.Count == 0)
        {
            return Task.FromResult(NodeHandlerOutput.Data(new DataBatch()));
        }

        // 先校验 Operation 合法性（早于字段校验，使未知 Operation 始终返回 UnknownOperation）。
        if (Operation is not (ListOpsOperation.Summarize or ListOpsOperation.FieldToItems or ListOpsOperation.ItemsToField or ListOpsOperation.GroupBy))
        {
            throw new NodeExecutionException("UnknownOperation", $"Unsupported operation: {Operation}");
        }

        // 参数校验。
        if (Operation == ListOpsOperation.GroupBy)
        {
            if (string.IsNullOrWhiteSpace(GroupBy))
            {
                throw new NodeExecutionException("MissingGroupBy", "GroupBy field is required for groupBy operation.");
            }

            if (Aggregate != ListOpsAggregate.Count && string.IsNullOrWhiteSpace(Field))
            {
                throw new NodeExecutionException("MissingField", "Field is required for groupBy aggregation other than count.");
            }
        }
        else if (string.IsNullOrWhiteSpace(Field))
        {
            throw new NodeExecutionException("MissingField", $"Field is required for {Operation} operation.");
        }

        var result = Operation switch
        {
            ListOpsOperation.Summarize => Summarize(inputBatch),
            ListOpsOperation.FieldToItems => FieldToItems(inputBatch),
            ListOpsOperation.ItemsToField => ItemsToField(inputBatch),
            ListOpsOperation.GroupBy => GroupByAggregate(inputBatch),
            _ => throw new NodeExecutionException("UnknownOperation", $"Unsupported operation: {Operation}")
        };

        return Task.FromResult(result);
    }

    /// <summary>
    /// 对指定数值字段做聚合，输出单条 <c>{ field, value, count }</c>。
    /// </summary>
    private NodeHandlerOutput Summarize(DataBatch inputBatch)
    {
        JsonNode valueNode;
        if (Aggregate == ListOpsAggregate.Count)
        {
            // count 不依赖字段数值。
            valueNode = JsonValue.Create((long)inputBatch.Items.Count);
        }
        else
        {
            var (values, originals) = CollectNumericValues(inputBatch, Field);
            valueNode = ComputeAggregate(values, originals, Aggregate);
        }

        var output = new JsonObject
        {
            ["field"] = Field,
            ["value"] = valueNode,
            ["count"] = inputBatch.Items.Count
        };

        return NodeHandlerOutput.Data(new DataBatch
        {
            Items =
            [
                new DataItem { Data = output, Success = true, SourceIndex = 0 }
            ]
        });
    }

    /// <summary>
    /// 将每个输入项中的数组字段拆分为多条输出项，每条输出 <c>{ value: element }</c>。
    /// </summary>
    private NodeHandlerOutput FieldToItems(DataBatch inputBatch)
    {
        var outputItems = new List<DataItem>();
        var index = 0;

        foreach (var item in inputBatch.Items)
        {
            var arrayNode = JsonPath.GetNode(item.Data, Field);
            if (arrayNode is not JsonArray array)
            {
                throw new NodeExecutionException("FieldNotArray", $"Field '{Field}' must be an array for fieldToItems operation.");
            }

            foreach (var element in array)
            {
                outputItems.Add(new DataItem
                {
                    Data = new JsonObject { ["value"] = element?.DeepClone() },
                    Success = true,
                    SourceIndex = index++
                });
            }
        }

        return NodeHandlerOutput.Data(new DataBatch { Items = outputItems });
    }

    /// <summary>
    /// 将多个输入项中同一字段的值收集为数组，输出单条 <c>{ field: [values] }</c>。
    /// </summary>
    private NodeHandlerOutput ItemsToField(DataBatch inputBatch)
    {
        var array = new JsonArray();
        foreach (var item in inputBatch.Items)
        {
            var value = JsonPath.GetNode(item.Data, Field);
            array.Add(value?.DeepClone());
        }

        var output = new JsonObject { [Field] = array };

        return NodeHandlerOutput.Data(new DataBatch
        {
            Items =
            [
                new DataItem { Data = output, Success = true, SourceIndex = 0 }
            ]
        });
    }

    /// <summary>
    /// 按分组字段分组，组内对数值字段做聚合，每组输出单条 <c>{ group, value }</c>。
    /// </summary>
    private NodeHandlerOutput GroupByAggregate(DataBatch inputBatch)
    {
        // key(string) -> (values, originals)
        var groups = new Dictionary<string, (List<decimal> Values, List<JsonNode> Originals)>();

        foreach (var item in inputBatch.Items)
        {
            var key = JsonPath.GetValue(item.Data, GroupBy) ?? string.Empty;
            if (!groups.TryGetValue(key, out var bucket))
            {
                bucket = (new List<decimal>(), new List<JsonNode>());
                groups[key] = bucket;
            }

            // count 聚合不读取字段值。
            if (Aggregate != ListOpsAggregate.Count)
            {
                if (!TryGetDecimal(JsonPath.GetNode(item.Data, Field), out var d))
                {
                    throw new NodeExecutionException("InvalidAggregateValue", $"Field '{Field}' is not numeric for item in group '{key}'.");
                }

                bucket.Values.Add(d);
                bucket.Originals.Add(JsonPath.GetNode(item.Data, Field)!);
            }
            else
            {
                bucket.Values.Add(0); // 占位，count 不依赖数值
            }
        }

        var outputItems = new List<DataItem>();
        var index = 0;
        foreach (var (key, bucket) in groups)
        {
            var valueNode = ComputeAggregate(bucket.Values, bucket.Originals, Aggregate);
            outputItems.Add(new DataItem
            {
                Data = new JsonObject
                {
                    ["group"] = key,
                    ["value"] = valueNode
                },
                Success = true,
                SourceIndex = index++
            });
        }

        return NodeHandlerOutput.Data(new DataBatch { Items = outputItems });
    }

    /// <summary>
    /// 收集字段的数值，遇到非数值立即抛出 <see cref="NodeExecutionException"/>（InvalidAggregateValue）。
    /// </summary>
    private static (List<decimal> Values, List<JsonNode> Originals) CollectNumericValues(DataBatch inputBatch, string field)
    {
        var values = new List<decimal>(inputBatch.Items.Count);
        var originals = new List<JsonNode>(inputBatch.Items.Count);

        foreach (var item in inputBatch.Items)
        {
            var node = JsonPath.GetNode(item.Data, field);
            if (!TryGetDecimal(node, out var d))
            {
                throw new NodeExecutionException("InvalidAggregateValue", $"Field '{field}' is not numeric.");
            }

            values.Add(d);
            originals.Add(node!);
        }

        return (values, originals);
    }

    /// <summary>
    /// 按聚合类型计算最终结果节点。
    /// </summary>
    private static JsonNode ComputeAggregate(List<decimal> values, List<JsonNode> originals, ListOpsAggregate aggregate)
    {
        return aggregate switch
        {
            ListOpsAggregate.Count => JsonValue.Create((long)values.Count),
            ListOpsAggregate.Sum => ToJsonNumber(values.Sum()),
            ListOpsAggregate.Avg => ToJsonNumber(values.Count == 0 ? 0m : values.Sum() / values.Count),
            ListOpsAggregate.Min => originals[MinMaxIndex(values, ascending: true)].DeepClone(),
            ListOpsAggregate.Max => originals[MinMaxIndex(values, ascending: false)].DeepClone(),
            _ => JsonValue.Create((long)values.Count)
        };
    }

    private static int MinMaxIndex(List<decimal> values, bool ascending)
    {
        var best = 0;
        for (var i = 1; i < values.Count; i++)
        {
            if (ascending ? values[i] < values[best] : values[i] > values[best])
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// 将 decimal 转为 JSON 数字节点：整数值优先以 long 输出（更干净），否则保留 decimal。
    /// </summary>
    private static JsonNode ToJsonNumber(decimal value) =>
        value == decimal.Truncate(value) && value >= long.MinValue && value <= long.MaxValue
            ? JsonValue.Create((long)value)!
            : JsonValue.Create(value)!;

    /// <summary>
    /// 尝试将 JSON 节点解析为 decimal（兼容 long/double/decimal 数值）。非数值返回 false。
    /// </summary>
    private static bool TryGetDecimal(JsonNode? node, out decimal value)
    {
        value = 0;
        if (node is not JsonValue jsonValue)
        {
            return false;
        }

        if (jsonValue.TryGetValue<decimal>(out var d))
        {
            value = d;
            return true;
        }

        if (jsonValue.TryGetValue<double>(out var db))
        {
            value = (decimal)db;
            return true;
        }

        return false;
    }
}

/// <summary>
/// listOps 节点的操作类型。
/// </summary>
public enum ListOpsOperation
{
    /// <summary>对指定数值字段做聚合，输出单条 { field, value, count }。</summary>
    [Description("Aggregate a numeric field into a single item { field, value, count }.")]
    Summarize,

    /// <summary>将每个输入项中的数组字段拆分为多条输出项，每条 { value: element }。</summary>
    [Description("Split an array field of each item into multiple items, each { value: element }.")]
    FieldToItems,

    /// <summary>将多个输入项中同一字段的值收集为数组，输出单条 { field: [values] }。</summary>
    [Description("Collect a field from all items into an array, output a single item { field: [values] }.")]
    ItemsToField,

    /// <summary>按分组字段分组并对数值字段做聚合，每组输出单条 { group, value }。</summary>
    [Description("Group items by a field and aggregate a numeric field within each group, output { group, value } per group.")]
    GroupBy
}

/// <summary>
/// listOps 节点的聚合函数。
/// </summary>
public enum ListOpsAggregate
{
    /// <summary>求和（要求数值字段）。</summary>
    [Description("Sum of numeric values.")]
    Sum,

    /// <summary>计数（不要求数值字段）。</summary>
    [Description("Count of items.")]
    Count,

    /// <summary>平均值（要求数值字段）。</summary>
    [Description("Average of numeric values.")]
    Avg,

    /// <summary>最小值（要求数值字段）。</summary>
    [Description("Minimum of numeric values.")]
    Min,

    /// <summary>最大值（要求数值字段）。</summary>
    [Description("Maximum of numeric values.")]
    Max
}
