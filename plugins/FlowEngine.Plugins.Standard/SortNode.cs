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
/// 排序节点，对数据进行排序。
/// </summary>
[NodeMeta(TypeName = "sort", DisplayName = "Sort", Category = NodeCategory.Data, Icon = "sort", DefaultIsEntry = false)]
[Port(FlowConstants.PortNames.Input, "Input", PortDirection.Input, PortType.Main)]
[Port(FlowConstants.PortNames.Output, "Output", PortDirection.Output, PortType.Main)]
public sealed class SortNode : NodeBase
{
    /// <summary>
    /// 排序字段列表。
    /// </summary>
    [Description("Fields to sort by.")]
    public List<SortField> SortFields { get; set; } = [];

    /// <inheritdoc />
    public override Task<NodeHandlerOutput> ExecuteAsync(NodeInput input, CancellationToken ct)
    {
        var inputBatch = input.InputBatch;

        if (SortFields.Count == 0)
        {
            return Task.FromResult(NodeHandlerOutput.Data(inputBatch));
        }

        IOrderedEnumerable<DataItem> sortedItems;

        if (SortFields[0].Direction == SortDirection.Asc)
        {
            sortedItems = inputBatch.Items
                .OrderBy(item => GetSortKey(item.Data, SortFields[0]));
        }
        else
        {
            sortedItems = inputBatch.Items
                .OrderByDescending(item => GetSortKey(item.Data, SortFields[0]));
        }

        // Apply secondary sort fields if any
        foreach (var field in SortFields.Skip(1))
        {
            if (field.Direction == SortDirection.Asc)
            {
                sortedItems = sortedItems.ThenBy(item => GetSortKey(item.Data, field));
            }
            else
            {
                sortedItems = sortedItems.ThenByDescending(item => GetSortKey(item.Data, field));
            }
        }

        return Task.FromResult(NodeHandlerOutput.Data(new DataBatch { Items = sortedItems.ToList() }));
    }

    private static SortKey GetSortKey(JsonNode? data, SortField field)
    {
        var value = JsonPath.GetValue(data, field.FieldName);
        return new SortKey(value);
    }

    /// <summary>
    /// 统一排序键，处理异构类型：同为数值时按数值比较，否则按字符串比较，避免类型不一致崩溃。
    /// </summary>
    private readonly struct SortKey : IComparable<SortKey>
    {
        private readonly string _stringValue;
        private readonly double? _numericValue;

        public SortKey(string? value)
        {
            _stringValue = value ?? string.Empty;
            _numericValue = double.TryParse(value, out var n) ? n : null;
        }

        public int CompareTo(SortKey other)
        {
            if (_numericValue.HasValue && other._numericValue.HasValue)
            {
                return _numericValue.Value.CompareTo(other._numericValue.Value);
            }

            return string.Compare(_stringValue, other._stringValue, StringComparison.Ordinal);
        }
    }

}

/// <summary>
/// 排序字段定义。
/// </summary>
public sealed class SortField
{
    /// <summary>
    /// 字段名称。
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// 排序方向。
    /// </summary>
    public SortDirection Direction { get; set; } = SortDirection.Asc;
}

/// <summary>
/// 排序方向。
/// </summary>
public enum SortDirection
{
    Asc,
    Desc
}
